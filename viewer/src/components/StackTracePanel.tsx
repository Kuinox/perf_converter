import { GitBranch, Layers } from "lucide-react";
import { useEffect, useMemo, useRef, useState, type CSSProperties, type PointerEvent, type WheelEvent } from "react";
import type { StackSlice, StackTimelineBin, TraceRow } from "../data/types";
import { dsoLabel, formatDurationNs, formatHex, formatTimeOffsetNs } from "../format";
import { useElementSize } from "../useResizeObserver";

interface StackTracePanelProps {
  rows: TraceRow[];
  slices?: StackSlice[];
  profileBins?: StackTimelineBin[];
  loading?: boolean;
  selectedRange?: { startTime: number; endTime: number } | null;
  timeRange: { startTime: number; endTime: number };
  originTime: number;
  onViewportChange?: (viewport: { startTime: number; endTime: number; minDurationNs: number }) => void;
}

interface ThreadLane {
  key: string;
  pid: number;
  tid: number;
  cpu: number;
  slices: StackSlice[];
  maxDepth: number;
}

const palette = [
  "#2dd4bf",
  "#60a5fa",
  "#a3e635",
  "#fbbf24",
  "#fb7185",
  "#c084fc",
  "#34d399",
  "#f97316",
  "#f472b6",
  "#22d3ee"
];
const rowHeight = 24;
const labelWidth = 178;
const maxThreadLanes = 18;
const maxRenderedDepth = 64;

export function StackTracePanel({
  rows,
  slices = [],
  profileBins = [],
  loading = false,
  selectedRange,
  timeRange,
  originTime,
  onViewportChange
}: StackTracePanelProps) {
  const { ref, size } = useElementSize<HTMLDivElement>();
  const [viewport, setViewport] = useState(timeRange);
  const dragRef = useRef<{ x: number; viewport: { startTime: number; endTime: number } } | null>(null);

  useEffect(() => {
    setViewport(timeRange);
  }, [timeRange.startTime, timeRange.endTime]);

  const minDurationNs = useMemo(() => {
    const span = Math.max(1, viewport.endTime - viewport.startTime);
    const width = Math.max(320, size.width || 1200);
    return Math.max(0, span / width * 0.65);
  }, [size.width, viewport.endTime, viewport.startTime]);

  useEffect(() => {
    onViewportChange?.({
      startTime: viewport.startTime,
      endTime: viewport.endTime,
      minDurationNs
    });
  }, [minDurationNs, onViewportChange, viewport.endTime, viewport.startTime]);

  const orderedSlices = [...slices].sort(
    (left, right) =>
      left.pid - right.pid ||
      left.tid - right.tid ||
      left.depth - right.depth ||
      left.startTime - right.startTime
  );

  if (!orderedSlices.length) {
    if (loading) {
      return (
        <div className="stack-empty">
          <Layers size={26} />
          <strong>Loading per-thread stack slices.</strong>
          <span>The stack index is being queried for the selected time window.</span>
        </div>
      );
    }

    const hasFallbackActivity = rows.length > 0 || profileBins.some((bin) => bin.samples > 0);
    return (
      <div className="stack-empty">
        <Layers size={26} />
        <strong>Stack slices appear after querying a timeline range.</strong>
        <span>
          {hasFallbackActivity
            ? "Use the stack index query to load a Perfetto-style per-thread view."
            : "Drag across the timeline, then query the stack for that window."}
        </span>
      </div>
    );
  }

  const viewMin = selectedRange
    ? Math.min(selectedRange.startTime, selectedRange.endTime)
    : viewport.startTime;
  const viewMax = selectedRange
    ? Math.max(selectedRange.startTime, selectedRange.endTime)
    : viewport.endTime;
  const span = Math.max(1, viewMax - viewMin);
  const lanes = buildThreadLanes(orderedSlices).slice(0, maxThreadLanes);
  const colors = new Map<string, string>();

  return (
    <div
      className="stack-trace perfetto-stack"
      ref={ref}
      onWheel={handleWheel}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerCancel}
    >
      <div className="stack-note">
        <GitBranch size={14} />
        <span>
          Wheel zooms the stack time axis. Drag pans. Hidden sub-pixel frames appear as you zoom in.
        </span>
      </div>

      <div className="stack-time-axis" style={{ "--label-width": `${labelWidth}px` } as CSSProperties}>
        <span />
        <div>
          <span>{formatTimeOffsetNs(viewMin, originTime)}</span>
          <span>{formatTimeOffsetNs(viewMax, originTime)}</span>
        </div>
      </div>

      <div className="stack-lane-list">
        {lanes.map((lane) => (
          <section className="thread-lane" key={lane.key}>
            <div className="thread-label">
              <strong>TID {lane.tid}</strong>
              <span>PID {lane.pid} · CPU {lane.cpu}</span>
            </div>
            <div
              className="thread-track"
              style={{
                height: `${Math.max(rowHeight, (Math.min(lane.maxDepth, maxRenderedDepth) + 1) * rowHeight)}px`
              }}
            >
              {lane.slices
                .filter((slice) => slice.depth <= maxRenderedDepth)
                .map((slice, index) => {
                  const start = Math.max(slice.startTime, viewMin);
                  const end = Math.min(slice.endTime, viewMax);
                  if (end < viewMin || start > viewMax) {
                    return null;
                  }

                  const left = ((start - viewMin) / span) * 100;
                  const width = Math.max(0.18, ((end - start) / span) * 100);
                  const frameKey = `${slice.dso}:${slice.relativeAddress}`;
                  const label = frameLabel(slice);
                  return (
                    <div
                      className="stack-slice"
                      key={`${slice.startTrace}:${slice.endTrace}:${slice.depth}:${index}`}
                      style={
                        {
                          "--slice-color": getFrameColor(frameKey, colors),
                          left: `${left}%`,
                          top: `${slice.depth * rowHeight + 2}px`,
                          width: `${width}%`
                        } as CSSProperties
                      }
                      title={`${label}
${dsoLabel(slice.dso)} ${formatHex(slice.relativeAddress)}
depth ${slice.depth}
${formatTimeOffsetNs(slice.startTime, originTime)} - ${formatTimeOffsetNs(slice.endTime, originTime)}`}
                    >
                      <span>{label}</span>
                    </div>
                  );
                })}
            </div>
          </section>
        ))}
      </div>
    </div>
  );

  function handleWheel(event: WheelEvent<HTMLDivElement>) {
    if (!onViewportChange) {
      return;
    }

    event.preventDefault();
    const rect = event.currentTarget.getBoundingClientRect();
    const timelineLeft = labelWidth;
    const timelineWidth = Math.max(1, rect.width - timelineLeft);
    const pointerX = Math.max(0, Math.min(timelineWidth, event.clientX - rect.left - timelineLeft));
    const anchor = viewport.startTime + (pointerX / timelineWidth) * (viewport.endTime - viewport.startTime);
    const zoom = Math.exp(event.deltaY * 0.0015);
    const targetSpan = clamp((viewport.endTime - viewport.startTime) * zoom, 1_000, timeRange.endTime - timeRange.startTime);
    const leftRatio = (anchor - viewport.startTime) / Math.max(1, viewport.endTime - viewport.startTime);
    setViewport(clampViewport(anchor - targetSpan * leftRatio, targetSpan));
  }

  function handlePointerDown(event: PointerEvent<HTMLDivElement>) {
    if (!onViewportChange || event.button !== 0) {
      return;
    }

    event.currentTarget.setPointerCapture(event.pointerId);
    dragRef.current = { x: event.clientX, viewport };
  }

  function handlePointerMove(event: PointerEvent<HTMLDivElement>) {
    const drag = dragRef.current;
    if (!drag) {
      return;
    }

    const width = Math.max(1, event.currentTarget.getBoundingClientRect().width - labelWidth);
    const span = drag.viewport.endTime - drag.viewport.startTime;
    const deltaTime = ((event.clientX - drag.x) / width) * span;
    setViewport(clampViewport(drag.viewport.startTime - deltaTime, span));
  }

  function handlePointerUp() {
    dragRef.current = null;
  }

  function handlePointerCancel() {
    dragRef.current = null;
  }

  function clampViewport(startTime: number, span: number): { startTime: number; endTime: number } {
    const fullSpan = Math.max(1, timeRange.endTime - timeRange.startTime);
    const boundedSpan = clamp(span, 1_000, fullSpan);
    const maxStart = timeRange.endTime - boundedSpan;
    const start = clamp(startTime, timeRange.startTime, maxStart);
    return { startTime: start, endTime: start + boundedSpan };
  }
}

function frameLabel(slice: StackSlice): string {
  if (slice.symbol?.trim()) {
    return slice.symbolOffset > 0 ? `${slice.symbol}+${formatHex(slice.symbolOffset)}` : slice.symbol;
  }

  return `${dsoLabel(slice.dso)} ${formatHex(slice.relativeAddress)}`;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function buildThreadLanes(slices: StackSlice[]): ThreadLane[] {
  const lanes = new Map<string, ThreadLane>();
  for (const slice of slices) {
    const key = `${slice.pid}:${slice.tid}`;
    const existing = lanes.get(key);
    if (existing) {
      existing.slices.push(slice);
      existing.maxDepth = Math.max(existing.maxDepth, slice.depth);
      continue;
    }

    lanes.set(key, {
      key,
      pid: slice.pid,
      tid: slice.tid,
      cpu: slice.cpu,
      slices: [slice],
      maxDepth: slice.depth
    });
  }

  return Array.from(lanes.values()).sort(
    (left, right) => right.slices.length - left.slices.length || left.pid - right.pid || left.tid - right.tid
  );
}

function getFrameColor(key: string, colors: Map<string, string>): string {
  const existing = colors.get(key);
  if (existing) {
    return existing;
  }

  let hash = 0;
  for (let index = 0; index < key.length; index++) {
    hash = (hash * 31 + key.charCodeAt(index)) >>> 0;
  }
  const color = palette[hash % palette.length];
  colors.set(key, color);
  return color;
}
