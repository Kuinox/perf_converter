import type {
  LoadedTraceSet,
  LocalTraceFile,
  StackIndexFile,
  SourceLocationFile,
  TraceManifestShard,
  TraceManifest,
  TraceSnapshot
} from "./types";

interface PickedFile {
  file: File;
  relativePath: string;
}

const tracePathPattern = /(?:^|\/)pid=(\d+)\/tid=(\d+)\/([^/]+)\.parquet$/i;

export async function parsePickedFiles(
  files: PickedFile[],
  rootLabel = "local trace"
): Promise<LoadedTraceSet> {
  const manifestEntry = files.find(
    (entry) => normalizePath(entry.relativePath).toLowerCase().endsWith("trace-manifest.json")
  );
  if (manifestEntry) {
    const fileMap = buildLocalFileMap(files, normalizePath(manifestEntry.relativePath));
    return manifestToTraceSet(
      JSON.parse(await manifestEntry.file.text()) as TraceManifest,
      manifestEntry.relativePath,
      fileMap
    );
  }

  const traces: LocalTraceFile[] = [];
  let sourceLocations: SourceLocationFile | undefined;
  let stackIndex: StackIndexFile | undefined;

  for (const picked of files) {
    const relativePath = normalizePath(picked.relativePath || picked.file.name);
    if (!relativePath.toLowerCase().endsWith(".parquet")) {
      continue;
    }

    if (relativePath.toLowerCase().endsWith("source_locations.parquet")) {
      sourceLocations = {
        kind: "local",
        file: picked.file,
        relativePath
      };
      continue;
    }

    if (relativePath.toLowerCase().endsWith("stack_index.parquet")) {
      stackIndex = {
        kind: "local",
        file: picked.file,
        relativePath,
        size: picked.file.size
      };
      continue;
    }

    const match = relativePath.match(tracePathPattern);
    if (!match) {
      continue;
    }

    const [, pidText, tidText, event] = match;
    const pid = Number(pidText);
    const tid = Number(tidText);
    const id = `${pid}:${tid}:${event}:${relativePath}`;
    traces.push({
      id,
      kind: "local",
      file: picked.file,
      relativePath,
      size: picked.file.size,
      pid,
      tid,
      event,
      registeredName: undefined
    });
  }

  traces.sort((left, right) => {
    const byPid = left.pid - right.pid;
    if (byPid) return byPid;
    const byTid = left.tid - right.tid;
    if (byTid) return byTid;
    return left.event.localeCompare(right.event);
  });

  return {
    mode: "live",
    rootLabel,
    files: traces,
    sourceLocations,
    stackIndex
  };
}

export function parseFileList(fileList: FileList): Promise<LoadedTraceSet> {
  const files = Array.from(fileList).map((file) => ({
    file,
    relativePath: getBrowserRelativePath(file)
  }));

  const rootLabel = inferRootLabel(files.map((entry) => entry.relativePath));
  return parsePickedFiles(files, rootLabel);
}

export async function pickDirectory(): Promise<LoadedTraceSet> {
  const picker = (window as unknown as {
    showDirectoryPicker?: () => Promise<FileSystemDirectoryHandle>;
  }).showDirectoryPicker;

  if (!picker) {
    throw new Error("Directory picker is not available in this browser.");
  }

  const root = await picker();
  const files: PickedFile[] = [];
  await collectDirectory(root, "", files);
  return parsePickedFiles(files, root.name || "local trace");
}

export async function readSnapshotFile(file: File): Promise<TraceSnapshot> {
  const value = JSON.parse(await file.text()) as TraceSnapshot;
  if (value.kind !== "perfconverter.trace-summary" || value.version !== 1) {
    throw new Error("This is not a PerfConverter trace-summary v1 file.");
  }

  return value;
}

export async function readManifestFile(file: File): Promise<LoadedTraceSet> {
  return manifestToTraceSet(JSON.parse(await file.text()) as TraceManifest, file.name);
}

export async function readTraceJsonFile(
  file: File
): Promise<{ type: "snapshot"; snapshot: TraceSnapshot } | { type: "manifest"; traceSet: LoadedTraceSet }> {
  const value = JSON.parse(await file.text()) as { kind?: string };
  if (value.kind === "perfconverter.trace-summary") {
    const snapshot = value as TraceSnapshot;
    if (snapshot.version !== 1) {
      throw new Error("Unsupported trace-summary version.");
    }
    return { type: "snapshot", snapshot };
  }

  if (value.kind === "perfconverter.trace-manifest") {
    return { type: "manifest", traceSet: manifestToTraceSet(value as TraceManifest, file.name) };
  }

  throw new Error("Expected a PerfConverter trace manifest or summary JSON file.");
}

export async function loadBundledSnapshot(): Promise<TraceSnapshot | null> {
  try {
    const response = await fetch("./trace-summary.json", { cache: "no-store" });
    if (!response.ok) {
      return null;
    }

    const value = (await response.json()) as TraceSnapshot;
    if (value.kind !== "perfconverter.trace-summary" || value.version !== 1) {
      return null;
    }

    return value;
  } catch {
    return null;
  }
}

export async function loadBundledManifest(): Promise<LoadedTraceSet | null> {
  try {
    const response = await fetch("./trace-manifest.json", { cache: "no-store" });
    if (!response.ok) {
      return null;
    }

    return manifestToTraceSet((await response.json()) as TraceManifest, "trace-manifest.json");
  } catch {
    return null;
  }
}

export function manifestToTraceSet(
  manifest: TraceManifest,
  manifestPath: string,
  localFiles?: Map<string, File>
): LoadedTraceSet {
  if (manifest.kind !== "perfconverter.trace-manifest" || manifest.version !== 1) {
    throw new Error("This is not a PerfConverter trace-manifest v1 file.");
  }

  const baseUrl = new URL(manifest.baseUrl ?? ".", window.location.href).toString();
  const files: LocalTraceFile[] = [];

  for (const stream of manifest.streams ?? []) {
    const relativePath = normalizePath(stream.path);
    files.push({
      id: `${stream.pid}:${stream.tid}:${stream.event}:${relativePath}`,
      kind: localFiles ? "local" : "remote",
      relativePath,
      size: stream.shards.reduce((sum, shard) => sum + (shard.size ?? 0), 0),
      pid: stream.pid,
      tid: stream.tid,
      event: stream.event,
      rows: stream.rows,
      minTime: stream.minTime,
      maxTime: stream.maxTime,
      minCpu: stream.minCpu ?? null,
      maxCpu: stream.maxCpu ?? null,
      shards: stream.shards.map((shard) => manifestShardToRef(shard, baseUrl, localFiles))
    });
  }

  for (const entry of manifest.files ?? []) {
    const relativePath = normalizePath(entry.path);
    const match = relativePath.match(tracePathPattern);
    if (!match) {
      continue;
    }

    const [, pidText, tidText, event] = match;
    const pid = Number(pidText);
    const tid = Number(tidText);
    files.push({
      id: `${pid}:${tid}:${event}:${relativePath}`,
      kind: localFiles ? "local" : "remote",
      file: localFiles?.get(relativePath),
      url: localFiles ? undefined : resolveManifestUrl(entry, baseUrl),
      relativePath,
      size: entry.size ?? 0,
      pid,
      tid,
      event
    });
  }

  const sourceLocations: SourceLocationFile | undefined = manifest.sourceLocations
    ? {
        kind: localFiles ? "local" : "remote",
        file: localFiles?.get(normalizePath(manifest.sourceLocations.path)),
        url: localFiles ? undefined : resolveManifestUrl(manifest.sourceLocations, baseUrl),
        relativePath: normalizePath(manifest.sourceLocations.path),
        size: manifest.sourceLocations.size
      }
    : undefined;

  const stackIndex: StackIndexFile | undefined = manifest.stackIndex
    ? {
        kind: localFiles ? "local" : "remote",
        file: localFiles?.get(normalizePath(manifest.stackIndex.path)),
        url: localFiles ? undefined : resolveManifestUrl(manifest.stackIndex, baseUrl),
        relativePath: normalizePath(manifest.stackIndex.path),
        size: manifest.stackIndex.size,
        shards: manifest.stackIndex.shards?.map((shard) =>
          manifestStackShardToRef(shard, baseUrl, localFiles)
        ),
        levels: manifest.stackIndex.levels?.map((level) => ({
          minDurationNs: level.minDurationNs,
          shards: level.shards.map((shard) => manifestStackShardToRef(shard, baseUrl, localFiles))
        }))
      }
    : undefined;

  return {
    mode: localFiles ? "live" : "remote",
    rootLabel: manifest.rootLabel ?? manifestPath,
    files,
    sourceLocations,
    stackIndex,
    overview: manifest.overview,
    profiles: manifest.profiles
  };
}

function buildLocalFileMap(files: PickedFile[], manifestPath: string): Map<string, File> {
  const fileMap = new Map<string, File>();
  const prefix = manifestPath.toLowerCase().endsWith("/trace-manifest.json")
    ? manifestPath.slice(0, -"trace-manifest.json".length)
    : "";

  for (const entry of files) {
    const relativePath = normalizePath(entry.relativePath);
    fileMap.set(relativePath, entry.file);
    if (prefix && relativePath.startsWith(prefix)) {
      fileMap.set(relativePath.slice(prefix.length), entry.file);
    }
  }

  return fileMap;
}

function manifestShardToRef(
  shard: TraceManifestShard,
  baseUrl: string,
  localFiles?: Map<string, File>
) {
  const relativePath = normalizePath(shard.path);
  return {
    kind: localFiles ? "local" as const : "remote" as const,
    file: localFiles?.get(relativePath),
    url: localFiles ? undefined : resolveManifestUrl(shard, baseUrl),
    relativePath,
    size: shard.size ?? 0,
    rows: shard.rows,
    minTime: shard.minTime,
    maxTime: shard.maxTime,
    minCpu: shard.minCpu ?? null,
    maxCpu: shard.maxCpu ?? null
  };
}

function manifestStackShardToRef(
  shard: TraceManifestShard,
  baseUrl: string,
  localFiles?: Map<string, File>
) {
  const relativePath = normalizePath(shard.path);
  return {
    kind: localFiles ? "local" as const : "remote" as const,
    file: localFiles?.get(relativePath),
    url: localFiles ? undefined : resolveManifestUrl(shard, baseUrl),
    relativePath,
    size: shard.size,
    rows: shard.rows,
    minTime: shard.minTime,
    maxTime: shard.maxTime
  };
}

function getBrowserRelativePath(file: File): string {
  const candidate = (file as File & { webkitRelativePath?: string }).webkitRelativePath;
  return normalizePath(candidate || file.name);
}

function normalizePath(path: string): string {
  return path.replaceAll("\\", "/").replace(/^\/+/, "");
}

function resolveManifestUrl(entry: { path: string; url?: string }, baseUrl: string): string {
  return new URL(entry.url ?? entry.path, baseUrl).toString();
}

function inferRootLabel(paths: string[]): string {
  const first = paths.find(Boolean);
  if (!first) {
    return "local trace";
  }

  const [root] = normalizePath(first).split("/");
  return root || "local trace";
}

async function collectDirectory(
  handle: FileSystemDirectoryHandle,
  prefix: string,
  output: PickedFile[]
): Promise<void> {
  const entries = (handle as unknown as {
    entries: () => AsyncIterableIterator<[string, FileSystemFileHandle | FileSystemDirectoryHandle]>;
  }).entries as () => AsyncIterableIterator<
    [string, FileSystemFileHandle | FileSystemDirectoryHandle]
  >;
  for await (const [name, entry] of entries.call(handle)) {
    const relativePath = prefix ? `${prefix}/${name}` : name;
    if (entry.kind === "file") {
      const file = await entry.getFile();
      output.push({ file, relativePath });
    } else {
      await collectDirectory(entry, relativePath, output);
    }
  }
}
