# PerfConverter Trace Viewer

Static browser viewer for PerfConverter traces. The browser reads a web-indexed dataset made of compact Parquet shards plus a manifest, so the UI can run on GitHub Pages without loading the raw multi-GB converter files into memory.

## Run Locally

```powershell
npm install
npm run dev
```

Open the shown URL, then choose either the raw `parquet_output` folder or the generated web trace folder. The raw folder should contain:

```text
source_locations.parquet
pid=*/tid=*/*.parquet
```

## Remote Parquet Mode

For hosted traces, keep the app on GitHub Pages and host the generated web trace folder somewhere that supports:

- HTTPS
- CORS for the Pages origin
- HTTP range requests
- Large objects

For small captures, a direct manifest can point at the raw files:

```powershell
npm run manifest -- C:\Users\Kuinox\Documents\parquet_output `
  --base-url https://example.com/perf-traces/run-001/ `
  --out public/trace-manifest.json `
  --label run-001
```

The manifest is tiny and can be committed with the Pages app. The Parquet files stay at the `base-url`.

For multi-GB captures, build the web trace index instead:

```powershell
dotnet run --project ..\StackFixer\StackFixer.csproj -- C:\Users\Kuinox\Documents\parquet_output

npm run web-index -- C:\Users\Kuinox\Documents\parquet_output `
  --out public/web-trace `
  --manifest public/trace-manifest.json `
  --base-url ./web-trace/ `
  --label run-001
```

The `StackFixer` pass writes `stack_index.parquet` from branch CALL/RETURN events. The generated manifest describes logical streams, time-sliced shards, and the stack index. The viewer profiles from offline metadata and only opens the shards or stack slices that intersect the selected zoom window.

## Build

```powershell
npm run build
```

The static site is emitted to `dist/`.

## Interaction Model

1. Open a capture folder or hosted manifest.
2. Inspect all pid/tid/event streams in the capture lanes.
3. Drag a time range across the timeline.
4. Click `Query rows` to fetch instruction or branch rows for that window.

The detail drill-down is SQL-backed; React only receives the current window of rows.
