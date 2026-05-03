export type LoadMode = "idle" | "live" | "remote" | "snapshot";

export interface LocalTraceFile {
  id: string;
  kind: "local" | "remote";
  file?: File;
  url?: string;
  relativePath: string;
  size: number;
  pid: number;
  tid: number;
  event: string;
  rows?: number;
  minTime?: number;
  maxTime?: number;
  minCpu?: number | null;
  maxCpu?: number | null;
  shards?: TraceShardRef[];
  registeredName?: string;
}

export interface TraceShardRef {
  kind: "local" | "remote";
  file?: File;
  url?: string;
  relativePath: string;
  size: number;
  rows: number;
  minTime: number;
  maxTime: number;
  minCpu: number | null;
  maxCpu: number | null;
  registeredName?: string;
}

export interface SourceLocationFile {
  kind: "local" | "remote";
  file?: File;
  url?: string;
  relativePath: string;
  size?: number;
  registeredName?: string;
}

export interface StackIndexFile {
  kind: "local" | "remote";
  file?: File;
  url?: string;
  relativePath: string;
  size?: number;
  shards?: StackIndexShardRef[];
  levels?: StackIndexLevelRef[];
  registeredName?: string;
}

export interface StackIndexShardRef {
  kind: "local" | "remote";
  file?: File;
  url?: string;
  relativePath: string;
  size?: number;
  rows: number;
  minTime: number;
  maxTime: number;
  registeredName?: string;
}

export interface StackIndexLevelRef {
  minDurationNs: number;
  shards: StackIndexShardRef[];
}

export interface TraceFileSummary {
  id: string;
  relativePath: string;
  size: number;
  pid: number;
  tid: number;
  event: string;
  rows: number;
  minTime: number;
  maxTime: number;
  minCpu: number | null;
  maxCpu: number | null;
  addressedRows: number;
  totalPeriod: number;
  totalInsn: number;
  totalCycles: number;
  rowsKnown?: boolean;
  timeKnown?: boolean;
}

export interface TraceOverview {
  totalRows: number;
  totalBytes: number;
  minTime: number;
  maxTime: number;
  pids: number[];
  tids: number[];
  events: string[];
  cpuMin: number | null;
  cpuMax: number | null;
  hasSourceLocations: boolean;
}

export interface TimelineBin {
  bin: number;
  startTime: number;
  endTime: number;
  samples: number;
  cpus: number;
  instructions: number;
  cycles: number;
}

export interface CpuBin {
  bin: number;
  cpu: number;
  samples: number;
}

export interface ModuleHotspot {
  dso: string;
  samples: number;
  addresses: number;
  kernelSamples: number;
  minAddress: number;
  maxAddress: number;
}

export interface AddressHotspot {
  dso: string;
  relativeAddress: number;
  samples: number;
  cpus: number;
  isKernel: boolean;
}

export interface BranchEdge {
  fromDso: string;
  fromAddress: number;
  toDso: string;
  toAddress: number;
  samples: number;
  cpus: number;
}

export interface StackTimelineFrame {
  dso: string;
  address: number;
  samples: number;
  cpus: number;
  isKernel?: boolean;
}

export interface StackTimelineBin {
  bin: number;
  startTime: number;
  endTime: number;
  samples: number;
  frames: StackTimelineFrame[];
}

export interface TraceRow {
  id: number;
  pid: number;
  tid: number;
  event: string;
  time: number;
  cpu: number;
  ip: number;
  addr: number;
  dso: string;
  relativeAddress: number | null;
  toDso: string;
  toAddress: number | null;
  instructions: number;
  cycles: number;
}

export interface StackSlice {
  pid: number;
  tid: number;
  cpu: number;
  depth: number;
  startTime: number;
  endTime: number;
  startTrace: number;
  endTrace: number;
  locationId: number;
  dso: string;
  symbol: string | null;
  relativeAddress: number;
  symbolOffset: number;
  isKernel: boolean;
}

export interface TraceProfile {
  fileId: string;
  fileLabel: string;
  generatedAt: string;
  timeline: TimelineBin[];
  cpuBins: CpuBin[];
  modules: ModuleHotspot[];
  addresses: AddressHotspot[];
  branches: BranchEdge[];
  stackTimeline?: StackTimelineBin[];
  notes: string[];
}

export interface TraceSnapshot {
  kind: "perfconverter.trace-summary";
  version: 1;
  generatedAt: string;
  rootLabel: string;
  overview: TraceOverview;
  files: TraceFileSummary[];
  profiles: Record<string, TraceProfile>;
}

export interface TraceManifestFile {
  path: string;
  url?: string;
  size?: number;
}

export interface TraceManifestStackIndex extends TraceManifestFile {
  shards?: TraceManifestShard[];
  levels?: Array<{ minDurationNs: number; shards: TraceManifestShard[] }>;
}

export interface TraceManifestShard extends TraceManifestFile {
  rows: number;
  minTime: number;
  maxTime: number;
  minCpu?: number | null;
  maxCpu?: number | null;
}

export interface TraceManifestStream {
  path: string;
  pid: number;
  tid: number;
  event: string;
  rows: number;
  minTime: number;
  maxTime: number;
  minCpu?: number | null;
  maxCpu?: number | null;
  shards: TraceManifestShard[];
}

export interface TraceManifest {
  kind: "perfconverter.trace-manifest";
  version: 1;
  rootLabel?: string;
  baseUrl?: string;
  sourceLocations?: TraceManifestFile;
  stackIndex?: TraceManifestStackIndex;
  files?: TraceManifestFile[];
  streams?: TraceManifestStream[];
  overview?: TraceOverview;
  profiles?: Record<string, TraceProfile>;
}

export interface LoadedTraceSet {
  mode: LoadMode;
  rootLabel: string;
  files: LocalTraceFile[];
  sourceLocations?: SourceLocationFile;
  stackIndex?: StackIndexFile;
  overview?: TraceOverview;
  profiles?: Record<string, TraceProfile>;
}

export interface LoadProgress {
  phase: string;
  completed: number;
  total: number;
  detail?: string;
}
