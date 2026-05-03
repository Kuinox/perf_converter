import { useEffect, useMemo, useRef, useState, type PointerEvent } from "react";
import { scaleLinear, scaleSequentialSqrt } from "d3";
import { interpolateTurbo } from "d3-scale-chromatic";
import type { TimelineBin } from "../data/types";
import { formatCompact, formatDurationNs } from "../format";
import { useElementSize } from "../useResizeObserver";

interface TimelineCanvasProps {
  bins: TimelineBin[];
  minTime: number;
  maxTime: number;
  selectedRange?: { startTime: number; endTime: number } | null;
  onRangeChange?: (range: { startTime: number; endTime: number }) => void;
}

export function TimelineCanvas({
  bins,
  minTime,
  maxTime,
  selectedRange,
  onRangeChange
}: TimelineCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const { ref, size } = useElementSize<HTMLDivElement>();
  const [hovered, setHovered] = useState<TimelineBin | null>(null);
  const [dragStart, setDragStart] = useState<number | null>(null);

  const maxSamples = useMemo(() => Math.max(1, ...bins.map((bin) => bin.samples)), [bins]);
  const maxCpus = useMemo(() => Math.max(1, ...bins.map((bin) => bin.cpus)), [bins]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || size.width <= 0) {
      return;
    }

    const width = Math.max(320, Math.floor(size.width));
    const height = 250;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = width * dpr;
    canvas.height = height * dpr;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;

    const ctx = canvas.getContext("2d");
    if (!ctx) {
      return;
    }

    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    const plotTop = 18;
    const plotBottom = height - 34;
    const plotHeight = plotBottom - plotTop;
    const x = scaleLinear([0, bins.length], [0, width]);
    const y = scaleLinear([0, Math.sqrt(maxSamples)], [plotBottom, plotTop]);
    const color = scaleSequentialSqrt(interpolateTurbo).domain([0, maxCpus]);

    const gradient = ctx.createLinearGradient(0, 0, 0, height);
    gradient.addColorStop(0, "rgba(20, 184, 166, 0.08)");
    gradient.addColorStop(0.5, "rgba(251, 191, 36, 0.05)");
    gradient.addColorStop(1, "rgba(244, 63, 94, 0.03)");
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, width, height);

    ctx.strokeStyle = "rgba(255,255,255,0.08)";
    ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
      const lineY = plotTop + (plotHeight * i) / 4;
      ctx.beginPath();
      ctx.moveTo(0, lineY);
      ctx.lineTo(width, lineY);
      ctx.stroke();
    }

    for (const bin of bins) {
      const left = x(bin.bin);
      const right = x(bin.bin + 1);
      const barWidth = Math.max(1, right - left - 1);
      const barHeight = plotBottom - y(Math.sqrt(bin.samples));
      ctx.fillStyle = bin.samples > 0 ? color(bin.cpus || 1) : "rgba(255,255,255,0.05)";
      ctx.fillRect(left, plotBottom - barHeight, barWidth, barHeight);
    }

    ctx.strokeStyle = "rgba(255,255,255,0.34)";
    ctx.beginPath();
    bins.forEach((bin, index) => {
      const px = x(bin.bin + 0.5);
      const py = y(Math.sqrt(bin.samples));
      if (index === 0) {
        ctx.moveTo(px, py);
      } else {
        ctx.lineTo(px, py);
      }
    });
    ctx.stroke();

    if (selectedRange) {
      const rangeStart = Math.max(minTime, Math.min(selectedRange.startTime, selectedRange.endTime));
      const rangeEnd = Math.min(maxTime, Math.max(selectedRange.startTime, selectedRange.endTime));
      const timeToX = scaleLinear([minTime, maxTime || minTime + 1], [0, width]);
      const left = timeToX(rangeStart);
      const right = timeToX(rangeEnd);
      ctx.fillStyle = "rgba(20, 184, 166, 0.16)";
      ctx.fillRect(left, plotTop, Math.max(2, right - left), plotHeight);
      ctx.strokeStyle = "rgba(94, 234, 212, 0.9)";
      ctx.strokeRect(left, plotTop, Math.max(2, right - left), plotHeight);
    }

    ctx.fillStyle = "rgba(245, 245, 240, 0.72)";
    ctx.font = "12px Inter, system-ui, sans-serif";
    ctx.fillText(formatDurationNs(maxTime - minTime), 12, height - 12);
    ctx.textAlign = "right";
    ctx.fillText(`${formatCompact(maxSamples)} peak samples/bin`, width - 12, height - 12);
  }, [bins, maxCpus, maxSamples, maxTime, minTime, selectedRange, size.width]);

  function onPointerMove(event: PointerEvent<HTMLCanvasElement>) {
    const canvas = canvasRef.current;
    if (!canvas) {
      return;
    }

    const rect = canvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const index = Math.max(0, Math.min(bins.length - 1, Math.floor((x / rect.width) * bins.length)));
    setHovered(bins[index]);

    if (dragStart !== null && onRangeChange) {
      const current = xToTime(x, rect.width);
      onRangeChange({ startTime: dragStart, endTime: current });
    }
  }

  function onPointerDown(event: PointerEvent<HTMLCanvasElement>) {
    const canvas = canvasRef.current;
    if (!canvas || !onRangeChange) {
      return;
    }

    canvas.setPointerCapture(event.pointerId);
    const rect = canvas.getBoundingClientRect();
    const time = xToTime(event.clientX - rect.left, rect.width);
    setDragStart(time);
    onRangeChange({ startTime: time, endTime: time });
  }

  function onPointerUp(event: PointerEvent<HTMLCanvasElement>) {
    if (dragStart === null || !onRangeChange) {
      return;
    }

    const canvas = canvasRef.current;
    const rect = canvas?.getBoundingClientRect();
    if (canvas && rect) {
      const time = xToTime(event.clientX - rect.left, rect.width);
      const startTime = Math.min(dragStart, time);
      const endTime = Math.max(dragStart, time);
      onRangeChange({
        startTime,
        endTime: endTime === startTime ? startTime + Math.max(1, (maxTime - minTime) / bins.length) : endTime
      });
    }

    setDragStart(null);
  }

  return (
    <div className="timeline-shell" ref={ref}>
      <canvas
        ref={canvasRef}
        className="timeline-canvas"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={() => setDragStart(null)}
        onPointerLeave={() => {
          setHovered(null);
          setDragStart(null);
        }}
      />
      {hovered ? (
        <div className="timeline-tooltip">
          <strong>Bin {hovered.bin}</strong>
          <span>{formatCompact(hovered.samples)} samples</span>
          <span>{formatCompact(hovered.cpus)} CPUs</span>
          <span>{formatDurationNs(hovered.startTime - minTime)} from start</span>
        </div>
      ) : null}
    </div>
  );

  function xToTime(x: number, width: number): number {
    const t = Math.max(0, Math.min(1, x / Math.max(1, width)));
    return minTime + (maxTime - minTime) * t;
  }
}
