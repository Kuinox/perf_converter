# PerfConverter Trace Viewer

Static browser viewer for PerfConverter Parquet traces. It uses DuckDB-WASM directly in the browser, so the UI runs on GitHub Pages while analytical queries run client-side against local or remote Parquet files.

## Run Locally

```powershell
npm install
npm run dev
```

Open the shown URL, then choose the `parquet_output` folder. The folder should contain:

```text
source_locations.parquet
pid=*/tid=*/*.parquet
```

## Remote Parquet Mode

For hosted traces, keep the app on GitHub Pages and host the multi-GB Parquet files somewhere that supports:

- HTTPS
- CORS for the Pages origin
- HTTP range requests
- Large objects

Generate a manifest:

```powershell
npm run manifest -- C:\Users\Kuinox\Documents\parquet_output `
  --base-url https://example.com/perf-traces/run-001/ `
  --out public/trace-manifest.json `
  --label run-001
```

The manifest is tiny and can be committed with the Pages app. The Parquet files stay at the `base-url`.

For responsive deep zoom on multi-GB traces, the hosted Parquet must either be sharded into web-sized files or include row-group min/max statistics for the filter columns (`time`, `id`, and `cpu`). DuckDB can skip Parquet row groups only when the file metadata gives it ranges to prune.

## Build

```powershell
npm run build
```

The static site is emitted to `dist/`.

## Interaction Model

1. Select a trace file from the left rail.
2. Click `Profile selected` to build overview queries.
3. Drag a time range across the timeline.
4. Click `Query rows` to fetch exact instruction or branch rows for that window.

The detail drill-down is SQL-backed; React only receives the current window of rows.
