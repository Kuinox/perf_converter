import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  BarChart3,
  Cpu,
  Database,
  FileJson,
  FolderOpen,
  Gauge,
  Layers,
  Loader2,
  Network,
  Rows3,
  Sparkles
} from "lucide-react";
import { AddressMap } from "./components/AddressMap";
import { BranchFlows } from "./components/BranchFlows";
import { CpuHeatmap } from "./components/CpuHeatmap";
import { ModuleBars } from "./components/ModuleBars";
import { StackTracePanel } from "./components/StackTracePanel";
import { TimelineCanvas } from "./components/TimelineCanvas";
import { TraceRowsTable } from "./components/TraceRowsTable";
import { getDuckDbClient } from "./data/duckdb";
import {
  loadBundledManifest,
  loadBundledSnapshot,
  parseFileList,
  pickDirectory,
  readTraceJsonFile
} from "./data/fileLoader";
import {
  buildOverview,
  profileTraceCapture,
  queryCaptureRows,
  queryStackSlices,
  summarizeTraceMetadata,
  summarizeTraceSet
} from "./data/queries";
import type {
  LoadedTraceSet,
  LoadProgress,
  StackSlice,
  TraceRow,
  TraceFileSummary,
  TraceOverview,
  TraceProfile,
  TraceSnapshot
} from "./data/types";
import { formatDurationNs } from "./format";

type StatusKind = "idle" | "busy" | "error" | "ready";

interface StatusState {
  kind: StatusKind;
  message: string;
}

export function App() {
  const folderInputRef = useRef<HTMLInputElement | null>(null);
  const snapshotInputRef = useRef<HTMLInputElement | null>(null);
  const stackRequestKeyRef = useRef<string | null>(null);
  const [traceSet, setTraceSet] = useState<LoadedTraceSet | null>(null);
  const [snapshot, setSnapshot] = useState<TraceSnapshot | null>(null);
  const [summaries, setSummaries] = useState<TraceFileSummary[]>([]);
  const [overview, setOverview] = useState<TraceOverview | null>(null);
  const [captureProfile, setCaptureProfile] = useState<TraceProfile | null>(null);
  const [selectedRange, setSelectedRange] = useState<{ startTime: number; endTime: number } | null>(
    null
  );
  const [detailRows, setDetailRows] = useState<TraceRow[]>([]);
  const [stackSlices, setStackSlices] = useState<StackSlice[]>([]);
  const [stackViewportRequest, setStackViewportRequest] = useState<{
    startTime: number;
    endTime: number;
    minDurationNs: number;
  } | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [progress, setProgress] = useState<LoadProgress | null>(null);
  const [status, setStatus] = useState<StatusState>({
    kind: "idle",
    message: "Load a PerfConverter Parquet output folder."
  });

  useEffect(() => {
    if (folderInputRef.current) {
      const input = folderInputRef.current as HTMLInputElement & {
        webkitdirectory?: boolean;
        directory?: boolean;
      };
      input.webkitdirectory = true;
      input.directory = true;
    }
  }, []);

  useEffect(() => {
    void loadBundledManifest().then(async (manifest) => {
      if (manifest) {
        await activateTraceSet(manifest);
        return;
      }

      const loaded = await loadBundledSnapshot();
      if (loaded) {
        activateSnapshot(loaded);
      }
    });
  }, []);

  const activeOverview = useMemo(
    () => buildOverview(summaries, Boolean(overview?.hasSourceLocations)),
    [overview?.hasSourceLocations, summaries]
  );

  const activeFileIds = useMemo(
    () => new Set(summaries.map((summary) => summary.id)),
    [summaries]
  );
  const activeFiles = useMemo(
    () => traceSet?.files.filter((file) => activeFileIds.has(file.id)) ?? [],
    [activeFileIds, traceSet]
  );
  const originTime = useMemo(
    () => captureProfile?.timeline[0]?.startTime ?? activeOverview.minTime,
    [activeOverview.minTime, captureProfile?.timeline]
  );

  const fullTimeRange = useMemo(
    () => ({
      startTime: captureProfile?.timeline[0]?.startTime ?? activeOverview.minTime,
      endTime:
        captureProfile?.timeline[captureProfile.timeline.length - 1]?.endTime ??
        activeOverview.maxTime
    }),
    [activeOverview.maxTime, activeOverview.minTime, captureProfile?.timeline]
  );

  const handleStackViewportChange = useCallback(
    (viewport: { startTime: number; endTime: number; minDurationNs: number }) => {
      setStackViewportRequest(viewport);
    },
    []
  );

  useEffect(() => {
    if (!traceSet?.stackIndex || !stackViewportRequest) {
      return;
    }

    const key = [
      Math.round(stackViewportRequest.startTime),
      Math.round(stackViewportRequest.endTime),
      Math.round(stackViewportRequest.minDurationNs)
    ].join(":");
    if (stackRequestKeyRef.current === key) {
      return;
    }

    const timeout = window.setTimeout(() => {
      stackRequestKeyRef.current = key;
      void runStackViewportQuery(stackViewportRequest);
    }, 160);

    return () => window.clearTimeout(timeout);
  }, [stackViewportRequest, traceSet?.stackIndex]);

  async function handleFolderInput(files: FileList | null) {
    if (!files?.length) {
      return;
    }

    await activateTraceSet(await parseFileList(files));
    if (folderInputRef.current) {
      folderInputRef.current.value = "";
    }
  }

  async function handleDirectoryPicker() {
    try {
      const picked = await pickDirectory();
      await activateTraceSet(picked);
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    }
  }

  async function handleSnapshotInput(files: FileList | null) {
    const file = files?.[0];
    if (!file) {
      return;
    }

    try {
      const parsed = await readTraceJsonFile(file);
      if (parsed.type === "snapshot") {
        activateSnapshot(parsed.snapshot);
      } else {
        await activateTraceSet(parsed.traceSet);
      }
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      if (snapshotInputRef.current) {
        snapshotInputRef.current.value = "";
      }
    }
  }

  async function activateTraceSet(nextTraceSet: LoadedTraceSet) {
    setSnapshot(null);
    setTraceSet(nextTraceSet);
    stackRequestKeyRef.current = null;
    setCaptureProfile(null);
    setDetailRows([]);
    setStackSlices([]);
    setStackViewportRequest(null);
    setSelectedRange(null);
    setStatus({ kind: "busy", message: "Preparing DuckDB-WASM." });
    setProgress({ phase: "Preparing DuckDB-WASM", completed: 0, total: 1 });

    if (!nextTraceSet.files.length) {
      setSummaries([]);
      setOverview(buildOverview([], Boolean(nextTraceSet.sourceLocations)));
      setStatus({ kind: "error", message: "No capture streams were found in this folder." });
      setProgress(null);
      return;
    }

    try {
      if (
        nextTraceSet.overview ||
        nextTraceSet.files.every(
          (file) =>
            file.rows !== undefined &&
            file.minTime !== undefined &&
            file.maxTime !== undefined
        )
      ) {
        const result = summarizeTraceMetadata(nextTraceSet);
        setSummaries(result.summaries);
        setOverview(nextTraceSet.overview ?? result.overview);
        setCaptureProfile(nextTraceSet.profiles?.capture ?? null);
        setStatus({
          kind: "ready",
          message: nextTraceSet.profiles?.capture ? "Capture loaded." : "Capture loaded. Ready to profile."
        });
        return;
      }

      const client = await getDuckDbClient();
      const result = await summarizeTraceSet(client, nextTraceSet, setProgress);
      setSummaries(result.summaries);
      setOverview(nextTraceSet.overview ?? result.overview);
      setCaptureProfile(nextTraceSet.profiles?.capture ?? null);
      setStatus({
        kind: "ready",
        message: nextTraceSet.profiles?.capture ? "Capture loaded." : "Capture loaded. Ready to profile."
      });
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      setProgress(null);
    }
  }

  function activateSnapshot(nextSnapshot: TraceSnapshot) {
    setSnapshot(nextSnapshot);
    stackRequestKeyRef.current = null;
    setTraceSet(null);
    setSummaries(nextSnapshot.files);
    setOverview(nextSnapshot.overview);
    setCaptureProfile(nextSnapshot.profiles.capture ?? Object.values(nextSnapshot.profiles)[0] ?? null);
    setDetailRows([]);
    setStackSlices([]);
    setStackViewportRequest(null);
    setSelectedRange(null);
    setStatus({
      kind: "ready",
      message: `Loaded snapshot generated ${new Date(nextSnapshot.generatedAt).toLocaleString()}.`
    });
    setProgress(null);
  }

  async function runCaptureProfile() {
    if (!traceSet || !summaries.length) {
      return;
    }

    if (traceSet.profiles?.capture) {
      setCaptureProfile(traceSet.profiles.capture);
      setStatus({ kind: "ready", message: "Capture profile ready." });
      return;
    }

    setStatus({ kind: "busy", message: "Profiling capture." });
    try {
      const client = await getDuckDbClient();
      const profile = await profileTraceCapture(
        client,
        activeFiles,
        summaries,
        traceSet.sourceLocations,
        setProgress
      );
      setSummaries((current) => {
        const next = [...current];
        setOverview(buildOverview(next, Boolean(traceSet.sourceLocations)));
        return next;
      });
      setCaptureProfile(profile);
      setStatus({
        kind: "ready",
        message: "Capture profile ready."
      });
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      setProgress(null);
    }
  }

  async function runDetailQuery(range: { startTime: number; endTime: number }) {
    if (!traceSet || !summaries.length) {
      return;
    }

    setSelectedRange(range);
    setDetailLoading(true);
    try {
      const client = await getDuckDbClient();
      if (traceSet.stackIndex) {
        const slices = await queryStackSlices(
          client,
          traceSet.stackIndex,
          range,
          traceSet.sourceLocations,
          18_000,
          Math.max(0, (Math.max(range.startTime, range.endTime) - Math.min(range.startTime, range.endTime)) / 1400)
        );
        setStackSlices(slices);
        setDetailRows([]);
        setStatus({
          kind: "ready",
          message: "Loaded stack slices for the selected window."
        });
        return;
      }

      const rows = await queryCaptureRows(
        client,
        activeFiles,
        summaries,
        range,
        traceSet.sourceLocations,
        600
      );
      setDetailRows(rows);
      setStackSlices([]);
      setStatus({
        kind: "ready",
        message: "Loaded instruction/branch rows for the selected window."
      });
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      setDetailLoading(false);
    }
  }

  async function runStackViewportQuery(viewport: {
    startTime: number;
    endTime: number;
    minDurationNs: number;
  }) {
    if (!traceSet?.stackIndex) {
      return;
    }

    setDetailLoading(true);
    try {
      const client = await getDuckDbClient();
      const slices = await queryStackSlices(
        client,
        traceSet.stackIndex,
        viewport,
        traceSet.sourceLocations,
        6_000,
        viewport.minDurationNs
      );
      setStackSlices(slices);
      setDetailRows([]);
      setStatus({
        kind: "ready",
        message: "Loaded stack slices for the visible stack viewport."
      });
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      setDetailLoading(false);
    }
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand">
          <div className="brand-mark">
            <Sparkles size={18} />
          </div>
          <div>
            <h1>PerfConverter Trace Viewer</h1>
            <span>{snapshot?.rootLabel ?? traceSet?.rootLabel ?? "Parquet browser workspace"}</span>
          </div>
        </div>
        <div className="topbar-actions">
          <input
            ref={folderInputRef}
            className="visually-hidden"
            type="file"
            multiple
            onChange={(event) => void handleFolderInput(event.target.files)}
          />
          <input
            ref={snapshotInputRef}
            className="visually-hidden"
            type="file"
            accept="application/json,.json"
            onChange={(event) => void handleSnapshotInput(event.target.files)}
          />
          {"showDirectoryPicker" in window ? (
            <button className="action-button" type="button" onClick={() => void handleDirectoryPicker()}>
              <FolderOpen size={16} />
              Open folder
            </button>
          ) : (
            <button className="action-button" type="button" onClick={() => folderInputRef.current?.click()}>
              <FolderOpen size={16} />
              Open folder
            </button>
          )}
          <button className="action-button secondary" type="button" onClick={() => snapshotInputRef.current?.click()}>
            <FileJson size={16} />
            Manifest
          </button>
        </div>
      </header>

      <section className={`status-line ${status.kind}`}>
        {status.kind === "busy" ? <Loader2 size={15} className="spin" /> : <Database size={15} />}
        <span>{status.message}</span>
        {progress ? (
          <strong>
            {progress.phase} {progress.total ? `${progress.completed}/${progress.total}` : ""}
          </strong>
        ) : null}
      </section>

      {!overview || !summaries.length ? (
        <section className="empty-dashboard">
          <div className="empty-panel">
            <Database size={34} />
            <h2>Open parquet_output</h2>
            <p>Choose a local output folder or load a remote trace-manifest.json file.</p>
            <button className="primary-large" type="button" onClick={() => folderInputRef.current?.click()}>
              <FolderOpen size={18} />
              Open capture folder
            </button>
          </div>
        </section>
      ) : (
        <section className="dashboard">
          <section className="overview-grid">
            <Metric icon={<Gauge size={18} />} label="Duration" value={formatDurationNs(overview.maxTime - overview.minTime)} detail="relative timeline" />
            <Metric icon={<Cpu size={18} />} label="CPU Range" value={formatCpuRange(overview)} detail="thread-aware stack" />
          </section>

          <section className="workspace-grid capture-workspace">
            <section className="main-pane">
              <div className="profile-header">
                <div>
                  <span className="eyebrow">Capture view</span>
                  <h2>Full trace capture</h2>
                </div>
                <div className="profile-actions">
                  <span className="profile-chip">all events</span>
                  {traceSet?.profiles?.capture ? (
                    <span className="profile-chip">profile loaded</span>
                  ) : traceSet ? (
                    <button
                      className="action-button"
                      type="button"
                      disabled={!summaries.length || status.kind === "busy"}
                      onClick={() => void runCaptureProfile()}
                    >
                      <BarChart3 size={16} />
                      Profile capture
                    </button>
                  ) : null}
                </div>
              </div>

              {captureProfile ? (
                <div className="profile-grid">
                  <section className="panel timeline-panel">
                    <PanelTitle icon={<ActivityIcon />} title="Temporal Density" />
                    <TimelineCanvas
                      bins={captureProfile.timeline}
                      minTime={captureProfile.timeline[0]?.startTime ?? activeOverview.minTime}
                      maxTime={
                        captureProfile.timeline[captureProfile.timeline.length - 1]?.endTime ??
                        activeOverview.maxTime
                      }
                      selectedRange={selectedRange}
                      onRangeChange={(range) => {
                        setSelectedRange(range);
                      }}
                    />
                    {traceSet ? (
                      <div className="timeline-actions">
                        <span>Drag across the timeline, then query the stack for that time window.</span>
                        <button
                          className="action-button"
                          disabled={!selectedRange || detailLoading}
                          onClick={() => selectedRange && void runDetailQuery(selectedRange)}
                          type="button"
                        >
                          <Rows3 size={16} />
                          Query stack
                        </button>
                      </div>
                    ) : null}
                  </section>

                  <section className="panel stack-panel">
                    <PanelTitle icon={<Layers size={16} />} title="Stack Trace Over Time" />
                    <StackTracePanel
                      rows={detailRows}
                      slices={stackSlices}
                      loading={detailLoading && Boolean(traceSet?.stackIndex)}
                      selectedRange={null}
                      timeRange={fullTimeRange}
                      originTime={originTime}
                      onViewportChange={traceSet?.stackIndex ? handleStackViewportChange : undefined}
                      profileBins={captureProfile.stackTimeline}
                    />
                  </section>

                  <section className="panel detail-panel">
                    <PanelTitle icon={<Rows3 size={16} />} title="Instruction / Branch Rows" />
                    <TraceRowsTable
                      rows={detailRows}
                      loading={detailLoading && !traceSet?.stackIndex}
                      originTime={originTime}
                    />
                  </section>

                  <section className="panel">
                    <PanelTitle icon={<Cpu size={16} />} title="CPU Heatmap" />
                    <CpuHeatmap bins={captureProfile.cpuBins} />
                  </section>

                  <section className="panel">
                    <PanelTitle icon={<Layers size={16} />} title="Module Hotspots" />
                    <ModuleBars modules={captureProfile.modules} />
                  </section>

                  <section className="panel">
                    <PanelTitle icon={<Network size={16} />} title="Branch Flow" />
                    <BranchFlows edges={captureProfile.branches} />
                  </section>

                  <section className="panel address-panel">
                    <PanelTitle icon={<Gauge size={16} />} title="Address Cartography" />
                    <AddressMap addresses={captureProfile.addresses} />
                  </section>

                </div>
              ) : (
                <section className="profile-empty">
                  <BarChart3 size={42} />
                  <h2>{snapshot ? "No saved capture profile" : "Profile the capture"}</h2>
                  <p>
                    {snapshot
                      ? "Load a snapshot that includes a capture profile."
                      : "Run one capture-level profile to compute the timeline, CPU lanes, module hotspots, hot addresses, and branch flow."}
                  </p>
                  {traceSet ? (
                    <button
                      className="primary-large"
                      type="button"
                      disabled={!summaries.length || status.kind === "busy"}
                      onClick={() => void runCaptureProfile()}
                    >
                      <BarChart3 size={18} />
                      Profile capture
                    </button>
                  ) : null}
                </section>
              )}
            </section>
          </section>
        </section>
      )}
    </main>
  );
}

function Metric({
  icon,
  label,
  value,
  detail
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <div className="metric">
      <div className="metric-icon">{icon}</div>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </div>
  );
}

function PanelTitle({ icon, title }: { icon: React.ReactNode; title: string }) {
  return (
    <div className="panel-title">
      {icon}
      <h3>{title}</h3>
    </div>
  );
}

function ActivityIcon() {
  return <BarChart3 size={16} />;
}

function formatCpuRange(overview: TraceOverview): string {
  if (overview.cpuMin === null || overview.cpuMax === null) {
    return "-";
  }

  return overview.cpuMin === overview.cpuMax
    ? `CPU ${overview.cpuMin}`
    : `${overview.cpuMin}-${overview.cpuMax}`;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
