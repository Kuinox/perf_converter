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

export interface TraceProfile {
  fileId: string;
  fileLabel: string;
  generatedAt: string;
  timeline: TimelineBin[];
  cpuBins: CpuBin[];
  modules: ModuleHotspot[];
  addresses: AddressHotspot[];
  branches: BranchEdge[];
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

export interface TraceManifest {
  kind: "perfconverter.trace-manifest";
  version: 1;
  rootLabel?: string;
  baseUrl?: string;
  sourceLocations?: TraceManifestFile;
  files: TraceManifestFile[];
}

export interface LoadedTraceSet {
  mode: LoadMode;
  rootLabel: string;
  files: LocalTraceFile[];
  sourceLocations?: SourceLocationFile;
}

export interface LoadProgress {
  phase: string;
  completed: number;
  total: number;
  detail?: string;
}
