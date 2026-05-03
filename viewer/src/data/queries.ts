import { DuckDbClient, escapeSql } from "./duckdb";
import type {
  AddressHotspot,
  BranchEdge,
  CpuBin,
  LoadedTraceSet,
  LoadProgress,
  LocalTraceFile,
  ModuleHotspot,
  SourceLocationFile,
  TimelineBin,
  TraceRow,
  TraceFileSummary,
  TraceOverview,
  TraceProfile
} from "./types";

const timelineBins = 160;
const heatmapBins = 120;
const eagerMetadataLimitBytes = 256 * 1024 * 1024;
const largeStreamIndexNote =
  "Some large streams need a prepared web index before exact timeline zoom can include them.";

type ProgressSink = (progress: LoadProgress) => void;

export async function summarizeTraceSet(
  client: DuckDbClient,
  traceSet: LoadedTraceSet,
  onProgress: ProgressSink
): Promise<{ overview: TraceOverview; summaries: TraceFileSummary[] }> {
  const summaries: TraceFileSummary[] = [];

  for (let index = 0; index < traceSet.files.length; index++) {
    const file = traceSet.files[index];
    onProgress({
      phase: "Loading capture",
      completed: index,
      total: traceSet.files.length,
      detail: file.relativePath
    });

    const summary: TraceFileSummary = {
      id: file.id,
      relativePath: file.relativePath,
      size: file.size,
      pid: file.pid,
      tid: file.tid,
      event: file.event,
      rows: 0,
      minTime: 0,
      maxTime: 0,
      minCpu: null,
      maxCpu: null,
      addressedRows: 0,
      totalPeriod: 0,
      totalInsn: 0,
      totalCycles: 0,
      rowsKnown: false,
      timeKnown: false
    };

    if (file.size > 0 && file.size <= eagerMetadataLimitBytes) {
      try {
        await hydrateSummary(client, file, summary, onProgress);
      } catch {
        // Keep folder-open cheap and robust. A failed metadata read should not block the trace list.
      }
    }

    summaries.push(summary);
  }

  onProgress({
      phase: "Ready",
    completed: traceSet.files.length,
    total: traceSet.files.length
  });

  return {
    overview: buildOverview(summaries, Boolean(traceSet.sourceLocations)),
    summaries
  };
}

export async function profileTraceFile(
  client: DuckDbClient,
  file: LocalTraceFile,
  summary: TraceFileSummary,
  sourceLocations: SourceLocationFile | undefined,
  onProgress: ProgressSink
): Promise<TraceProfile> {
  if (!summary.timeKnown && summary.size > eagerMetadataLimitBytes) {
    throw new Error(
      "This stream needs a prepared web index before it can be profiled in-browser."
    );
  }

  if (!summary.timeKnown) {
    await hydrateSummary(client, file, summary, onProgress);
  }

  const registeredName =
    file.registeredName ?? (await registerTraceFile(client, file));
  file.registeredName = registeredName;

  if (sourceLocations && !sourceLocations.registeredName) {
    sourceLocations.registeredName = await registerSourceLocationFile(client, sourceLocations);
  }

  const notes: string[] = [];
  const sampleStride = getSampleStride(summary.rows);
  if (sampleStride > 1) {
    notes.push(
      `Large trace mode: profile queries sample every ${sampleStride} rows and scale counts.`
    );
  }

  const traceSource = sampledTraceSource(client.parquet(registeredName), sampleStride);

  onProgress({ phase: "Building timeline", completed: 0, total: 5, detail: summary.relativePath });
  const timeline = await queryTimeline(client, traceSource, summary, sampleStride);

  onProgress({ phase: "Building CPU heatmap", completed: 1, total: 5, detail: summary.relativePath });
  const cpuBins = await queryCpuBins(client, traceSource, summary, sampleStride);

  onProgress({ phase: "Resolving module hotspots", completed: 2, total: 5, detail: summary.relativePath });
  const modules = await queryModules(client, traceSource, sourceLocations, sampleStride, notes);

  onProgress({ phase: "Mapping hot addresses", completed: 3, total: 5, detail: summary.relativePath });
  const addresses = await queryAddresses(client, traceSource, sourceLocations, sampleStride, notes);

  onProgress({ phase: "Tracing branch flows", completed: 4, total: 5, detail: summary.relativePath });
  const branches = file.event.toLowerCase().includes("branch")
    ? await queryBranches(client, traceSource, sourceLocations, sampleStride, notes)
    : [];

  onProgress({ phase: "Profile ready", completed: 5, total: 5, detail: summary.relativePath });

  return {
    fileId: file.id,
    fileLabel: `${file.event} | pid ${file.pid} / tid ${file.tid}`,
    generatedAt: new Date().toISOString(),
    timeline,
    cpuBins,
    modules,
    addresses,
    branches,
    notes
  };
}

export async function profileTraceCapture(
  client: DuckDbClient,
  files: LocalTraceFile[],
  summaries: TraceFileSummary[],
  sourceLocations: SourceLocationFile | undefined,
  onProgress: ProgressSink
): Promise<TraceProfile> {
  const summaryById = new Map(summaries.map((summary) => [summary.id, summary]));
  const notes: string[] = [];
  const profileInputs: Array<{
    file: LocalTraceFile;
    summary: TraceFileSummary;
    traceSource: string;
    sampleStride: number;
  }> = [];

  for (let index = 0; index < files.length; index++) {
    const file = files[index];
    const summary = summaryById.get(file.id);
    if (!summary) {
      continue;
    }

    onProgress({
      phase: "Preparing capture",
      completed: index,
      total: files.length,
      detail: file.relativePath
    });

    if (!summary.timeKnown) {
      if (summary.size > eagerMetadataLimitBytes) {
        await hydrateFooterSummary(client, file, summary);
        pushUnique(notes, largeStreamIndexNote);
        continue;
      }

      await hydrateSummary(client, file, summary, onProgress);
    }

    const registeredName = file.registeredName ?? (await registerTraceFile(client, file));
    file.registeredName = registeredName;
    const sampleStride = getSampleStride(summary.rows);
    if (sampleStride > 1) {
      pushUnique(notes, `Large capture mode samples every ${sampleStride} rows for overview charts.`);
    }

    profileInputs.push({
      file,
      summary,
      traceSource: sampledTraceSource(client.parquet(registeredName), sampleStride),
      sampleStride
    });
  }

  if (sourceLocations && !sourceLocations.registeredName) {
    sourceLocations.registeredName = await registerSourceLocationFile(client, sourceLocations);
  }

  if (!profileInputs.length) {
    throw new Error(
      "This capture needs a prepared web index before it can be profiled in-browser."
    );
  }

  const captureBounds = {
    minTime: Math.min(...profileInputs.map((input) => input.summary.minTime)),
    maxTime: Math.max(...profileInputs.map((input) => input.summary.maxTime))
  };

  const timeline = createEmptyTimeline(captureBounds.minTime, captureBounds.maxTime);
  const cpuBins = new Map<string, CpuBin>();
  const modules = new Map<string, ModuleHotspot>();
  const addresses = new Map<string, AddressHotspot>();
  const branches = new Map<string, BranchEdge>();

  for (let index = 0; index < profileInputs.length; index++) {
    const input = profileInputs[index];
    const boundsSummary = {
      ...input.summary,
      minTime: captureBounds.minTime,
      maxTime: captureBounds.maxTime
    };

    onProgress({
      phase: "Profiling capture",
      completed: index,
      total: profileInputs.length,
      detail: input.summary.relativePath
    });

    mergeTimeline(
      timeline,
      await queryTimeline(client, input.traceSource, boundsSummary, input.sampleStride)
    );
    mergeCpuBins(
      cpuBins,
      await queryCpuBins(client, input.traceSource, boundsSummary, input.sampleStride)
    );
    mergeModules(
      modules,
      await queryModules(client, input.traceSource, sourceLocations, input.sampleStride, notes)
    );
    mergeAddresses(
      addresses,
      await queryAddresses(client, input.traceSource, sourceLocations, input.sampleStride, notes)
    );

    if (input.file.event.toLowerCase().includes("branch")) {
      mergeBranches(
        branches,
        await queryBranches(client, input.traceSource, sourceLocations, input.sampleStride, notes)
      );
    }
  }

  onProgress({
    phase: "Capture profile ready",
    completed: profileInputs.length,
    total: profileInputs.length
  });

  return {
    fileId: "capture",
    fileLabel: "Full capture",
    generatedAt: new Date().toISOString(),
    timeline,
    cpuBins: Array.from(cpuBins.values()).sort((left, right) => left.cpu - right.cpu || left.bin - right.bin),
    modules: Array.from(modules.values()).sort((left, right) => right.samples - left.samples).slice(0, 28),
    addresses: Array.from(addresses.values()).sort((left, right) => right.samples - left.samples).slice(0, 80),
    branches: Array.from(branches.values()).sort((left, right) => right.samples - left.samples).slice(0, 48),
    notes
  };
}

export async function queryTraceRows(
  client: DuckDbClient,
  file: LocalTraceFile,
  range: { startTime: number; endTime: number },
  sourceLocations: SourceLocationFile | undefined,
  limit: number
): Promise<TraceRow[]> {
  const registeredName = file.registeredName ?? (await registerTraceFile(client, file));
  file.registeredName = registeredName;

  if (sourceLocations && !sourceLocations.registeredName) {
    sourceLocations.registeredName = await registerSourceLocationFile(client, sourceLocations);
  }

  const startTime = Math.min(range.startTime, range.endTime);
  const endTime = Math.max(range.startTime, range.endTime);
  const baseTrace = `
    SELECT id, time, cpu, ip, addr, ipLocationId, addressLocationId, insnCnt, cycCnt
    FROM ${client.parquet(registeredName)}
    WHERE time >= ${startTime} AND time <= ${endTime}
    ORDER BY time, id
    LIMIT ${Math.max(1, Math.min(10_000, Math.trunc(limit)))}
  `;

  if (!sourceLocations?.registeredName) {
    return client.query<TraceRow>(`
      WITH trace AS (${baseTrace})
      SELECT
        CAST(id AS DOUBLE) AS id,
        ${file.pid} AS pid,
        ${file.tid} AS tid,
        '${escapeSql(file.event)}' AS event,
        CAST(time AS DOUBLE) AS time,
        CAST(cpu AS INTEGER) AS cpu,
        CAST(ip AS DOUBLE) AS ip,
        CAST(addr AS DOUBLE) AS addr,
        '[unresolved]' AS dso,
        NULL AS relativeAddress,
        '[unresolved]' AS toDso,
        NULL AS toAddress,
        CAST(coalesce(insnCnt, 0) AS DOUBLE) AS instructions,
        CAST(coalesce(cycCnt, 0) AS DOUBLE) AS cycles
      FROM trace
    `);
  }

  return client.query<TraceRow>(`
    WITH
      trace AS (${baseTrace}),
      loc AS (${sourceLocationSource(client, sourceLocations.registeredName)})
    SELECT
      CAST(trace.id AS DOUBLE) AS id,
      ${file.pid} AS pid,
      ${file.tid} AS tid,
      '${escapeSql(file.event)}' AS event,
      CAST(trace.time AS DOUBLE) AS time,
      CAST(trace.cpu AS INTEGER) AS cpu,
      CAST(trace.ip AS DOUBLE) AS ip,
      CAST(trace.addr AS DOUBLE) AS addr,
      coalesce(nullif(src.dso, ''), '[address only]') AS dso,
      CAST(src.relativeAddress AS DOUBLE) AS relativeAddress,
      coalesce(nullif(dst.dso, ''), '[address only]') AS toDso,
      CAST(dst.relativeAddress AS DOUBLE) AS toAddress,
      CAST(coalesce(trace.insnCnt, 0) AS DOUBLE) AS instructions,
      CAST(coalesce(trace.cycCnt, 0) AS DOUBLE) AS cycles
    FROM trace
    LEFT JOIN loc src ON trace.ipLocationId = src.id
    LEFT JOIN loc dst ON trace.addressLocationId = dst.id
    ORDER BY trace.time, trace.id
  `);
}

export async function queryCaptureRows(
  client: DuckDbClient,
  files: LocalTraceFile[],
  summaries: TraceFileSummary[],
  range: { startTime: number; endTime: number },
  sourceLocations: SourceLocationFile | undefined,
  limit: number
): Promise<TraceRow[]> {
  const summaryById = new Map(summaries.map((summary) => [summary.id, summary]));
  const eligibleFiles = files.filter((file) => {
    const summary = summaryById.get(file.id);
    return (
      summary?.timeKnown &&
      Math.max(summary.minTime, Math.min(range.startTime, range.endTime)) <=
        Math.min(summary.maxTime, Math.max(range.startTime, range.endTime))
    );
  });

  if (!eligibleFiles.length) {
    return [];
  }

  const perFileLimit = Math.max(20, Math.ceil(limit / eligibleFiles.length));
  const rows: TraceRow[] = [];
  for (const file of eligibleFiles) {
    const remaining = limit - rows.length;
    if (remaining <= 0) {
      break;
    }

    rows.push(
      ...(await queryTraceRows(
        client,
        file,
        range,
        sourceLocations,
        Math.min(perFileLimit, remaining)
      ))
    );
  }

  return rows.sort((left, right) => left.time - right.time || left.id - right.id).slice(0, limit);
}

export function buildOverview(
  summaries: TraceFileSummary[],
  hasSourceLocations: boolean
): TraceOverview {
  const totalRows = summaries.reduce((sum, file) => sum + (file.rowsKnown ? file.rows : 0), 0);
  const totalBytes = summaries.reduce((sum, file) => sum + file.size, 0);
  const times = summaries.filter((file) => file.timeKnown && file.rows > 0);
  const minTime = times.length ? Math.min(...times.map((file) => file.minTime)) : 0;
  const maxTime = times.length ? Math.max(...times.map((file) => file.maxTime)) : 0;
  const cpuMins = summaries
    .map((file) => file.minCpu)
    .filter((value): value is number => value !== null);
  const cpuMaxes = summaries
    .map((file) => file.maxCpu)
    .filter((value): value is number => value !== null);

  return {
    totalRows,
    totalBytes,
    minTime,
    maxTime,
    pids: uniqueSorted(summaries.map((file) => file.pid)),
    tids: uniqueSorted(summaries.map((file) => file.tid)),
    events: Array.from(new Set(summaries.map((file) => file.event))).sort(),
    cpuMin: cpuMins.length ? Math.min(...cpuMins) : null,
    cpuMax: cpuMaxes.length ? Math.max(...cpuMaxes) : null,
    hasSourceLocations
  };
}

async function hydrateSummary(
  client: DuckDbClient,
  file: LocalTraceFile,
  summary: TraceFileSummary,
  onProgress: ProgressSink
): Promise<void> {
  onProgress({
    phase: "Preparing stream",
    completed: 0,
    total: 1,
    detail: file.relativePath
  });

  const registeredName = file.registeredName ?? (await registerTraceFile(client, file));
  file.registeredName = registeredName;
  const rows = await client.query<{
    rowCount: number;
    minTime: number | null;
    maxTime: number | null;
    minCpu: number | null;
    maxCpu: number | null;
  }>(`
    SELECT
      CAST(count(*) AS DOUBLE) AS rowCount,
      CAST(min(time) AS DOUBLE) AS minTime,
      CAST(max(time) AS DOUBLE) AS maxTime,
      CAST(min(cpu) AS INTEGER) AS minCpu,
      CAST(max(cpu) AS INTEGER) AS maxCpu
    FROM ${client.parquet(registeredName)}
  `);

  const row = rows[0];
  summary.rows = Number(row.rowCount ?? 0);
  summary.minTime = Number(row.minTime ?? 0);
  summary.maxTime = Number(row.maxTime ?? 0);
  summary.minCpu = row.minCpu === null ? null : Number(row.minCpu);
  summary.maxCpu = row.maxCpu === null ? null : Number(row.maxCpu);
  summary.rowsKnown = true;
  summary.timeKnown = true;
}

async function hydrateFooterSummary(
  client: DuckDbClient,
  file: LocalTraceFile,
  summary: TraceFileSummary
): Promise<void> {
  if (summary.rowsKnown) {
    return;
  }

  const registeredName = file.registeredName ?? (await registerTraceFile(client, file));
  file.registeredName = registeredName;

  try {
    const rows = await client.query<{ rowCount: number }>(`
      SELECT CAST(num_rows AS DOUBLE) AS rowCount
      FROM parquet_file_metadata('${escapeSql(registeredName)}')
    `);
    summary.rows = Number(rows[0]?.rowCount ?? summary.rows);
    summary.rowsKnown = summary.rows > 0;
  } catch {
    // Footer metadata is an optimization only.
  }
}

async function queryTimeline(
  client: DuckDbClient,
  traceSource: string,
  summary: TraceFileSummary,
  sampleStride: number
): Promise<TimelineBin[]> {
  const span = Math.max(1, summary.maxTime - summary.minTime + 1);
  const rows = await client.query<{
    bin: number;
    startTime: number;
    endTime: number;
    samples: number;
    cpus: number;
    instructions: number;
    cycles: number;
  }>(`
    WITH trace AS (${traceSource})
    SELECT
      CAST(LEAST(${timelineBins - 1}, GREATEST(0, FLOOR(((CAST(time AS DOUBLE) - ${summary.minTime}) / ${span}) * ${timelineBins}))) AS INTEGER) AS bin,
      CAST(min(time) AS DOUBLE) AS startTime,
      CAST(max(time) AS DOUBLE) AS endTime,
      CAST(count(*) * ${sampleStride} AS DOUBLE) AS samples,
      CAST(approx_count_distinct(cpu) AS DOUBLE) AS cpus,
      CAST(coalesce(sum(insnCnt), 0) * ${sampleStride} AS DOUBLE) AS instructions,
      CAST(coalesce(sum(cycCnt), 0) * ${sampleStride} AS DOUBLE) AS cycles
    FROM trace
    GROUP BY 1
    ORDER BY 1
  `);

  return fillTimeline(rows, summary);
}

async function queryCpuBins(
  client: DuckDbClient,
  traceSource: string,
  summary: TraceFileSummary,
  sampleStride: number
): Promise<CpuBin[]> {
  const span = Math.max(1, summary.maxTime - summary.minTime + 1);
  return client.query<CpuBin>(`
    WITH trace AS (${traceSource})
    SELECT
      CAST(LEAST(${heatmapBins - 1}, GREATEST(0, FLOOR(((CAST(time AS DOUBLE) - ${summary.minTime}) / ${span}) * ${heatmapBins}))) AS INTEGER) AS bin,
      CAST(cpu AS INTEGER) AS cpu,
      CAST(count(*) * ${sampleStride} AS DOUBLE) AS samples
    FROM trace
    GROUP BY 1, 2
    ORDER BY 2, 1
  `);
}

async function queryModules(
  client: DuckDbClient,
  traceSource: string,
  sourceLocations: SourceLocationFile | undefined,
  sampleStride: number,
  notes: string[]
): Promise<ModuleHotspot[]> {
  if (!sourceLocations?.registeredName) {
    notes.push("No source_locations.parquet file was loaded; module names are unavailable.");
    return [];
  }

  try {
    return await client.query<ModuleHotspot>(`
      WITH
        trace AS (${traceSource}),
        loc AS (${sourceLocationSource(client, sourceLocations.registeredName)})
      SELECT
        coalesce(nullif(loc.dso, ''), '[address only]') AS dso,
        CAST(count(*) * ${sampleStride} AS DOUBLE) AS samples,
        CAST(approx_count_distinct(loc.relativeAddress) AS DOUBLE) AS addresses,
        CAST(sum(CASE WHEN loc.isKernelIp <> 0 THEN 1 ELSE 0 END) * ${sampleStride} AS DOUBLE) AS kernelSamples,
        CAST(min(loc.relativeAddress) AS DOUBLE) AS minAddress,
        CAST(max(loc.relativeAddress) AS DOUBLE) AS maxAddress
      FROM trace
      LEFT JOIN loc ON trace.ipLocationId = loc.id
      GROUP BY 1
      ORDER BY samples DESC
      LIMIT 28
    `);
  } catch (error) {
    notes.push(`Module query failed: ${errorMessage(error)}`);
    return [];
  }
}

async function queryAddresses(
  client: DuckDbClient,
  traceSource: string,
  sourceLocations: SourceLocationFile | undefined,
  sampleStride: number,
  notes: string[]
): Promise<AddressHotspot[]> {
  if (!sourceLocations?.registeredName) {
    return [];
  }

  try {
    return await client.query<AddressHotspot>(`
      WITH
        trace AS (${traceSource}),
        loc AS (${sourceLocationSource(client, sourceLocations.registeredName)})
      SELECT
        coalesce(nullif(loc.dso, ''), '[address only]') AS dso,
        CAST(loc.relativeAddress AS DOUBLE) AS relativeAddress,
        CAST(count(*) * ${sampleStride} AS DOUBLE) AS samples,
        CAST(approx_count_distinct(trace.cpu) AS DOUBLE) AS cpus,
        CAST(max(CASE WHEN loc.isKernelIp <> 0 THEN 1 ELSE 0 END) AS BOOLEAN) AS isKernel
      FROM trace
      LEFT JOIN loc ON trace.ipLocationId = loc.id
      WHERE trace.ipLocationId <> 0
      GROUP BY 1, 2
      ORDER BY samples DESC
      LIMIT 80
    `);
  } catch (error) {
    notes.push(`Address query failed: ${errorMessage(error)}`);
    return [];
  }
}

async function queryBranches(
  client: DuckDbClient,
  traceSource: string,
  sourceLocations: SourceLocationFile | undefined,
  sampleStride: number,
  notes: string[]
): Promise<BranchEdge[]> {
  if (!sourceLocations?.registeredName) {
    return [];
  }

  try {
    return await client.query<BranchEdge>(`
      WITH
        trace AS (${traceSource}),
        loc AS (${sourceLocationSource(client, sourceLocations.registeredName)})
      SELECT
        coalesce(nullif(src.dso, ''), '[address only]') AS fromDso,
        CAST(coalesce(src.relativeAddress, trace.ip) AS DOUBLE) AS fromAddress,
        coalesce(nullif(dst.dso, ''), '[address only]') AS toDso,
        CAST(coalesce(dst.relativeAddress, trace.addr) AS DOUBLE) AS toAddress,
        CAST(count(*) * ${sampleStride} AS DOUBLE) AS samples,
        CAST(approx_count_distinct(trace.cpu) AS DOUBLE) AS cpus
      FROM trace
      LEFT JOIN loc src ON trace.ipLocationId = src.id
      LEFT JOIN loc dst ON trace.addressLocationId = dst.id
      WHERE trace.addressLocationId <> 0
      GROUP BY 1, 2, 3, 4
      ORDER BY samples DESC
      LIMIT 48
    `);
  } catch (error) {
    notes.push(`Branch query failed: ${errorMessage(error)}`);
    return [];
  }
}

function sampledTraceSource(parquetSource: string, stride: number): string {
  if (stride <= 1) {
    return `
      SELECT id, time, cpu, ip, addr, ipLocationId, addressLocationId, insnCnt, cycCnt, period
      FROM ${parquetSource}
    `;
  }

  return `
    SELECT id, time, cpu, ip, addr, ipLocationId, addressLocationId, insnCnt, cycCnt, period
    FROM ${parquetSource}
    WHERE id % ${stride} = 0
  `;
}

function sourceLocationSource(client: DuckDbClient, registeredName: string): string {
  return `
    SELECT
      id,
      decode(dso) AS dso,
      relativeAddress,
      isKernelIp
    FROM ${client.parquet(registeredName)}
  `;
}

function fillTimeline(rows: TimelineBin[], summary: TraceFileSummary): TimelineBin[] {
  const byBin = new Map(rows.map((row) => [row.bin, row]));
  const span = Math.max(1, summary.maxTime - summary.minTime + 1);
  const output: TimelineBin[] = [];

  for (let bin = 0; bin < timelineBins; bin++) {
    const binStart = summary.minTime + (span * bin) / timelineBins;
    const binEnd = summary.minTime + (span * (bin + 1)) / timelineBins;
    output.push(
      byBin.get(bin) ?? {
        bin,
        startTime: binStart,
        endTime: binEnd,
        samples: 0,
        cpus: 0,
        instructions: 0,
        cycles: 0
      }
    );
  }

  return output;
}

function createEmptyTimeline(minTime: number, maxTime: number): TimelineBin[] {
  const span = Math.max(1, maxTime - minTime + 1);
  return Array.from({ length: timelineBins }, (_, bin) => ({
    bin,
    startTime: minTime + (span * bin) / timelineBins,
    endTime: minTime + (span * (bin + 1)) / timelineBins,
    samples: 0,
    cpus: 0,
    instructions: 0,
    cycles: 0
  }));
}

function mergeTimeline(target: TimelineBin[], source: TimelineBin[]): void {
  for (const bin of source) {
    const current = target[bin.bin];
    if (!current) {
      continue;
    }

    current.samples += bin.samples;
    current.cpus = Math.max(current.cpus, bin.cpus);
    current.instructions += bin.instructions;
    current.cycles += bin.cycles;
  }
}

function mergeCpuBins(target: Map<string, CpuBin>, source: CpuBin[]): void {
  for (const bin of source) {
    const key = `${bin.cpu}:${bin.bin}`;
    const existing = target.get(key);
    if (existing) {
      existing.samples += bin.samples;
    } else {
      target.set(key, { ...bin });
    }
  }
}

function mergeModules(target: Map<string, ModuleHotspot>, source: ModuleHotspot[]): void {
  for (const module of source) {
    const existing = target.get(module.dso);
    if (existing) {
      existing.samples += module.samples;
      existing.addresses += module.addresses;
      existing.kernelSamples += module.kernelSamples;
      existing.minAddress = Math.min(existing.minAddress, module.minAddress);
      existing.maxAddress = Math.max(existing.maxAddress, module.maxAddress);
    } else {
      target.set(module.dso, { ...module });
    }
  }
}

function mergeAddresses(target: Map<string, AddressHotspot>, source: AddressHotspot[]): void {
  for (const address of source) {
    const key = `${address.dso}:${address.relativeAddress}`;
    const existing = target.get(key);
    if (existing) {
      existing.samples += address.samples;
      existing.cpus = Math.max(existing.cpus, address.cpus);
      existing.isKernel = existing.isKernel || address.isKernel;
    } else {
      target.set(key, { ...address });
    }
  }
}

function mergeBranches(target: Map<string, BranchEdge>, source: BranchEdge[]): void {
  for (const edge of source) {
    const key = `${edge.fromDso}:${edge.fromAddress}:${edge.toDso}:${edge.toAddress}`;
    const existing = target.get(key);
    if (existing) {
      existing.samples += edge.samples;
      existing.cpus = Math.max(existing.cpus, edge.cpus);
    } else {
      target.set(key, { ...edge });
    }
  }
}

function pushUnique(notes: string[], note: string): void {
  if (!notes.includes(note)) {
    notes.push(note);
  }
}

function getSampleStride(rows: number): number {
  if (rows <= 50_000_000) {
    return 1;
  }

  return Math.ceil(rows / 25_000_000);
}

async function registerTraceFile(client: DuckDbClient, file: LocalTraceFile): Promise<string> {
  if (file.registeredName) {
    return file.registeredName;
  }

  if (file.kind === "remote") {
    if (!file.url) {
      throw new Error(`Remote trace file is missing a URL: ${file.relativePath}`);
    }

    return client.registerUrl(file.url, file.relativePath);
  }

  if (!file.file) {
    throw new Error(`Local trace file is missing a File handle: ${file.relativePath}`);
  }

  return client.registerFile(file.file, file.relativePath);
}

async function registerSourceLocationFile(
  client: DuckDbClient,
  file: SourceLocationFile
): Promise<string> {
  if (file.registeredName) {
    return file.registeredName;
  }

  if (file.kind === "remote") {
    if (!file.url) {
      throw new Error(`Remote source location file is missing a URL: ${file.relativePath}`);
    }

    return client.registerUrl(file.url, file.relativePath);
  }

  if (!file.file) {
    throw new Error(`Local source location file is missing a File handle: ${file.relativePath}`);
  }

  return client.registerFile(file.file, file.relativePath);
}

function uniqueSorted(values: number[]): number[] {
  return Array.from(new Set(values)).sort((left, right) => left - right);
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function fileSqlLiteral(fileName: string): string {
  return `'${escapeSql(fileName)}'`;
}
