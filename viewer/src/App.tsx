import { useEffect, useMemo, useRef, useState } from "react";
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
  Sparkles,
  Zap
} from "lucide-react";
import { AddressMap } from "./components/AddressMap";
import { BranchFlows } from "./components/BranchFlows";
import { CaptureLanes } from "./components/CaptureLanes";
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
  summarizeTraceSet
} from "./data/queries";
import type {
  LoadedTraceSet,
  LoadProgress,
  TraceRow,
  TraceFileSummary,
  TraceOverview,
  TraceProfile,
  TraceSnapshot
} from "./data/types";
import { formatCompact, formatDurationNs, formatInteger } from "./format";

type StatusKind = "idle" | "busy" | "error" | "ready";

interface StatusState {
  kind: StatusKind;
  message: string;
}

export function App() {
  const folderInputRef = useRef<HTMLInputElement | null>(null);
  const snapshotInputRef = useRef<HTMLInputElement | null>(null);
  const [traceSet, setTraceSet] = useState<LoadedTraceSet | null>(null);
  const [snapshot, setSnapshot] = useState<TraceSnapshot | null>(null);
  const [summaries, setSummaries] = useState<TraceFileSummary[]>([]);
  const [overview, setOverview] = useState<TraceOverview | null>(null);
  const [captureProfile, setCaptureProfile] = useState<TraceProfile | null>(null);
  const [selectedRange, setSelectedRange] = useState<{ startTime: number; endTime: number } | null>(
    null
  );
  const [detailRows, setDetailRows] = useState<TraceRow[]>([]);
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

  async function handleFolderInput(files: FileList | null) {
    if (!files?.length) {
      return;
    }

    await activateTraceSet(parseFileList(files));
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
    setCaptureProfile(null);
    setDetailRows([]);
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
      const client = await getDuckDbClient();
      const result = await summarizeTraceSet(client, nextTraceSet, setProgress);
      setSummaries(result.summaries);
      setOverview(result.overview);
      setStatus({
        kind: "ready",
        message: "Capture loaded. Ready to profile."
      });
    } catch (error) {
      setStatus({ kind: "error", message: errorMessage(error) });
    } finally {
      setProgress(null);
    }
  }

  function activateSnapshot(nextSnapshot: TraceSnapshot) {
    setSnapshot(nextSnapshot);
    setTraceSet(null);
    setSummaries(nextSnapshot.files);
    setOverview(nextSnapshot.overview);
    setCaptureProfile(nextSnapshot.profiles.capture ?? Object.values(nextSnapshot.profiles)[0] ?? null);
    setDetailRows([]);
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
      const rows = await queryCaptureRows(
        client,
        activeFiles,
        summaries,
        range,
        traceSet.sourceLocations,
        600
      );
      setDetailRows(rows);
      setStatus({
        kind: "ready",
        message: `Loaded ${formatInteger(rows.length)} instruction/branch rows for the selected window.`
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
            <Metric icon={<Zap size={18} />} label="Rows" value={formatCompact(overview.totalRows)} detail={overview.totalRows ? formatInteger(overview.totalRows) : "profile to populate"} />
            <Metric icon={<Gauge size={18} />} label="Duration" value={formatDurationNs(overview.maxTime - overview.minTime)} detail={`${overview.events.length} events`} />
            <Metric icon={<Cpu size={18} />} label="CPU Range" value={formatCpuRange(overview)} detail={`${overview.tids.length} threads`} />
            <Metric icon={<Database size={18} />} label="Streams" value={formatCompact(summaries.length)} detail="capture-wide view" />
          </section>

          <section className="workspace-grid capture-workspace">
            <section className="main-pane">
              <section className="panel lanes-panel">
                <PanelTitle icon={<Layers size={16} />} title="Capture Lanes" />
                <CaptureLanes streams={summaries} />
              </section>

              <div className="profile-header">
                <div>
                  <span className="eyebrow">Capture view</span>
                  <h2>Full trace capture</h2>
                  <p>
                    {summaries.length} streams,{" "}
                    {activeOverview.totalRows
                      ? `${formatCompact(activeOverview.totalRows)} known rows`
                      : "profile to populate the timeline"}
                  </p>
                </div>
                <div className="profile-actions">
                  <span className="profile-chip">all events</span>
                  {traceSet ? (
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
                        <span>Drag across the timeline, then query the visible instruction/branch rows.</span>
                        <button
                          className="action-button"
                          disabled={!selectedRange || detailLoading}
                          onClick={() => selectedRange && void runDetailQuery(selectedRange)}
                          type="button"
                        >
                          <Rows3 size={16} />
                          Query rows
                        </button>
                      </div>
                    ) : null}
                  </section>

                  <section className="panel detail-panel">
                    <PanelTitle icon={<Rows3 size={16} />} title="Instruction / Branch Rows" />
                    <TraceRowsTable rows={detailRows} loading={detailLoading} />
                  </section>

                  <section className="panel stack-panel">
                    <PanelTitle icon={<Layers size={16} />} title="Stack Trace" />
                    <StackTracePanel rows={detailRows} />
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

                  {captureProfile.notes.length ? (
                    <section className="panel notes-panel">
                      <PanelTitle icon={<FileJson size={16} />} title="Query Notes" />
                      <ul>
                        {captureProfile.notes.map((note) => (
                          <li key={note}>{note}</li>
                        ))}
                      </ul>
                    </section>
                  ) : null}
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
