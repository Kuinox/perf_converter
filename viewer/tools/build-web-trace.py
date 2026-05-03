#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import math
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

import numpy as np
import pyarrow as pa
import pyarrow.parquet as pq


TRACE_COLUMNS = [
    "id",
    "time",
    "cpu",
    "ip",
    "addr",
    "ipLocationId",
    "addressLocationId",
    "insnCnt",
    "cycCnt",
]
SOURCE_LOCATION_COLUMNS = ["id", "dso", "relativeAddress", "symbol", "symbolOffset", "isKernelIp"]
TRACE_PATTERN = re.compile(r"(?:^|/)pid=(\d+)/tid=(\d+)/([^/]+)\.parquet$", re.IGNORECASE)
TIMELINE_BINS = 160
HEATMAP_BINS = 120


@dataclass
class StreamInput:
    path: Path
    pid: int
    tid: int
    event: str
    rows: int = 0
    min_time: int | None = None
    max_time: int | None = None
    min_cpu: int | None = None
    max_cpu: int | None = None


@dataclass
class ShardWriter:
    writer: pq.ParquetWriter
    path: Path
    relative_path: str
    rows: int = 0
    min_time: int | None = None
    max_time: int | None = None
    min_cpu: int | None = None
    max_cpu: int | None = None

    def write(self, table: pa.Table, row_group_size: int) -> None:
        self.writer.write_table(table, row_group_size=row_group_size)
        times = table.column("time").to_numpy(zero_copy_only=False)
        cpus = table.column("cpu").to_numpy(zero_copy_only=False)
        self.rows += table.num_rows
        self.min_time = min_optional(self.min_time, int(times.min()))
        self.max_time = max_optional(self.max_time, int(times.max()))
        self.min_cpu = min_optional(self.min_cpu, int(cpus.min()))
        self.max_cpu = max_optional(self.max_cpu, int(cpus.max()))

    def close(self) -> dict:
        self.writer.close()
        return {
            "path": self.relative_path,
            "size": self.path.stat().st_size,
            "rows": self.rows,
            "minTime": self.min_time or 0,
            "maxTime": self.max_time or 0,
            "minCpu": self.min_cpu,
            "maxCpu": self.max_cpu,
        }


class ProfileAccumulator:
    def __init__(self, min_time: int, max_time: int, source_locations: dict[int, tuple[str, int, bool]]) -> None:
        self.min_time = min_time
        self.max_time = max(max_time, min_time)
        self.span = max(1, self.max_time - self.min_time + 1)
        self.source_locations = source_locations
        self.timeline_samples = np.zeros(TIMELINE_BINS, dtype=np.float64)
        self.timeline_instructions = np.zeros(TIMELINE_BINS, dtype=np.float64)
        self.timeline_cycles = np.zeros(TIMELINE_BINS, dtype=np.float64)
        self.timeline_cpus: list[set[int]] = [set() for _ in range(TIMELINE_BINS)]
        self.cpu_bins: Counter[tuple[int, int]] = Counter()
        self.module_samples: Counter[str] = Counter()
        self.module_kernel_samples: Counter[str] = Counter()
        self.module_addresses: defaultdict[str, set[int]] = defaultdict(set)
        self.module_min_address: dict[str, int] = {}
        self.module_max_address: dict[str, int] = {}
        self.address_samples: Counter[tuple[str, int, bool]] = Counter()
        self.address_cpus: defaultdict[tuple[str, int, bool], set[int]] = defaultdict(set)
        self.branch_samples: Counter[tuple[str, int, str, int]] = Counter()
        self.branch_cpus: defaultdict[tuple[str, int, str, int], set[int]] = defaultdict(set)

    def update(self, table: pa.Table, event: str, sample_stride: int) -> None:
        ids = table.column("id").to_numpy(zero_copy_only=False)
        times = table.column("time").to_numpy(zero_copy_only=False)
        cpus = table.column("cpu").to_numpy(zero_copy_only=False).astype(np.int64, copy=False)
        insn = table.column("insnCnt").to_numpy(zero_copy_only=False)
        cycles = table.column("cycCnt").to_numpy(zero_copy_only=False)

        timeline_bins = self._bins(times, TIMELINE_BINS)
        self.timeline_samples += np.bincount(timeline_bins, minlength=TIMELINE_BINS)
        self.timeline_instructions += np.bincount(timeline_bins, weights=insn, minlength=TIMELINE_BINS)
        self.timeline_cycles += np.bincount(timeline_bins, weights=cycles, minlength=TIMELINE_BINS)
        for bin_index, cpu in np.unique(np.column_stack((timeline_bins, cpus)), axis=0):
            self.timeline_cpus[int(bin_index)].add(int(cpu))

        heatmap_bins = self._bins(times, HEATMAP_BINS)
        unique_pairs, counts = np.unique(np.column_stack((cpus, heatmap_bins)), axis=0, return_counts=True)
        for (cpu, bin_index), count in zip(unique_pairs, counts):
            self.cpu_bins[(int(cpu), int(bin_index))] += int(count)

        mask = ids % max(1, sample_stride) == 0
        if not mask.any():
            return

        ip_locations = table.column("ipLocationId").to_numpy(zero_copy_only=False)[mask]
        address_locations = table.column("addressLocationId").to_numpy(zero_copy_only=False)[mask]
        sampled_cpus = cpus[mask]
        scale = max(1, sample_stride)

        for location_id, cpu in zip(ip_locations, sampled_cpus):
            dso, relative_address, is_kernel = self.source_locations.get(int(location_id), ("[address only]", 0, False))
            self.module_samples[dso] += scale
            if is_kernel:
                self.module_kernel_samples[dso] += scale
            if relative_address:
                self.module_addresses[dso].add(relative_address)
                self.module_min_address[dso] = min_optional(self.module_min_address.get(dso), relative_address)
                self.module_max_address[dso] = max_optional(self.module_max_address.get(dso), relative_address)
                address_key = (dso, relative_address, is_kernel)
                self.address_samples[address_key] += scale
                self.address_cpus[address_key].add(int(cpu))

        if "branch" in event.lower():
            for source_id, target_id, cpu in zip(ip_locations, address_locations, sampled_cpus):
                if int(target_id) == 0:
                    continue
                source = self.source_locations.get(int(source_id), ("[address only]", 0, False))
                target = self.source_locations.get(int(target_id), ("[address only]", 0, False))
                branch_key = (source[0], source[1], target[0], target[1])
                self.branch_samples[branch_key] += scale
                self.branch_cpus[branch_key].add(int(cpu))

    def to_profile(self, stack_timeline: list[dict] | None = None) -> dict:
        timeline = []
        for bin_index in range(TIMELINE_BINS):
            start = self.min_time + (self.span * bin_index) / TIMELINE_BINS
            end = self.min_time + (self.span * (bin_index + 1)) / TIMELINE_BINS
            timeline.append(
                {
                    "bin": bin_index,
                    "startTime": start,
                    "endTime": end,
                    "samples": float(self.timeline_samples[bin_index]),
                    "cpus": len(self.timeline_cpus[bin_index]),
                    "instructions": float(self.timeline_instructions[bin_index]),
                    "cycles": float(self.timeline_cycles[bin_index]),
                }
            )

        modules = []
        for dso, samples in self.module_samples.most_common(28):
            modules.append(
                {
                    "dso": dso,
                    "samples": samples,
                    "addresses": len(self.module_addresses.get(dso, set())),
                    "kernelSamples": self.module_kernel_samples.get(dso, 0),
                    "minAddress": self.module_min_address.get(dso, 0),
                    "maxAddress": self.module_max_address.get(dso, 0),
                }
            )

        addresses = []
        for (dso, relative_address, is_kernel), samples in self.address_samples.most_common(80):
            key = (dso, relative_address, is_kernel)
            addresses.append(
                {
                    "dso": dso,
                    "relativeAddress": relative_address,
                    "samples": samples,
                    "cpus": len(self.address_cpus.get(key, set())),
                    "isKernel": is_kernel,
                }
            )

        branches = []
        for (from_dso, from_address, to_dso, to_address), samples in self.branch_samples.most_common(48):
            key = (from_dso, from_address, to_dso, to_address)
            branches.append(
                {
                    "fromDso": from_dso,
                    "fromAddress": from_address,
                    "toDso": to_dso,
                    "toAddress": to_address,
                    "samples": samples,
                    "cpus": len(self.branch_cpus.get(key, set())),
                }
            )

        return {
            "fileId": "capture",
            "fileLabel": "Full capture",
            "generatedAt": current_iso(),
            "timeline": timeline,
            "cpuBins": [
                {"cpu": cpu, "bin": bin_index, "samples": samples}
                for (cpu, bin_index), samples in sorted(self.cpu_bins.items())
            ],
            "modules": modules,
            "addresses": addresses,
            "branches": branches,
            "stackTimeline": stack_timeline or [],
            "notes": ["Profile built offline from the web trace index."],
        }

    def _bins(self, times: np.ndarray, count: int) -> np.ndarray:
        bins = np.floor(((times.astype(np.float64) - self.min_time) / self.span) * count).astype(np.int64)
        return np.clip(bins, 0, count - 1)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Convert PerfConverter parquet_output into a browser-safe sharded web trace."
    )
    parser.add_argument("parquet_output", type=Path, help="Raw PerfConverter parquet_output folder")
    parser.add_argument("--out", type=Path, default=Path("public/web-trace"), help="Output dataset folder")
    parser.add_argument(
        "--manifest",
        type=Path,
        default=Path("public/trace-manifest.json"),
        help="Manifest path consumed by the viewer",
    )
    parser.add_argument("--base-url", default="./web-trace/", help="URL prefix for the output dataset")
    parser.add_argument("--label", default=None, help="Trace label shown by the viewer")
    parser.add_argument("--rows-per-shard", type=int, default=1_000_000)
    parser.add_argument("--row-group-size", type=int, default=128_000)
    parser.add_argument("--batch-size", type=int, default=512_000)
    parser.add_argument("--profile-sample-target", type=int, default=2_000_000)
    parser.add_argument("--compression", default="zstd")
    args = parser.parse_args()

    source_root = args.parquet_output.resolve()
    output_root = args.out.resolve()
    manifest_path = args.manifest.resolve()

    if not source_root.exists():
        raise SystemExit(f"Input folder does not exist: {source_root}")
    if output_root.exists() and any(output_root.iterdir()):
        raise SystemExit(f"Output folder already exists and is not empty: {output_root}")
    if manifest_path.exists():
        raise SystemExit(f"Manifest already exists: {manifest_path}")

    streams = discover_streams(source_root)
    if not streams:
        raise SystemExit(f"No trace streams found under {source_root}")

    output_root.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"Discovered {len(streams)} streams")
    copy_source_locations(source_root, output_root, args.compression, args.row_group_size)
    source_locations = load_source_locations(output_root / "source_locations.parquet")
    stack_index = copy_stack_index(source_root, output_root, args.compression, args.row_group_size)

    print("Scanning capture bounds")
    for index, stream in enumerate(streams, start=1):
        scan_stream_stats(stream, args.batch_size)
        print(
            f"[{index}/{len(streams)}] {stream.pid}/{stream.tid}/{stream.event}: "
            f"{stream.rows:,} rows"
        )

    global_min_time = min(stream.min_time for stream in streams if stream.min_time is not None)
    global_max_time = max(stream.max_time for stream in streams if stream.max_time is not None)
    profile = ProfileAccumulator(global_min_time, global_max_time, source_locations)

    manifest_streams = []
    for index, stream in enumerate(streams, start=1):
        print(f"Exporting [{index}/{len(streams)}] pid {stream.pid} tid {stream.tid} {stream.event}")
        shards = export_stream(
            stream,
            source_root,
            output_root,
            profile,
            rows_per_shard=args.rows_per_shard,
            row_group_size=args.row_group_size,
            batch_size=args.batch_size,
            sample_target=args.profile_sample_target,
            compression=args.compression,
        )
        manifest_streams.append(
            {
                "path": f"pid={stream.pid}/tid={stream.tid}/{stream.event}",
                "pid": stream.pid,
                "tid": stream.tid,
                "event": stream.event,
                "rows": stream.rows,
                "minTime": stream.min_time,
                "maxTime": stream.max_time,
                "minCpu": stream.min_cpu,
                "maxCpu": stream.max_cpu,
                "shards": shards,
            }
        )

    overview = build_overview(manifest_streams, has_source_locations=(output_root / "source_locations.parquet").exists())
    stack_timeline = build_stack_timeline(
        output_root / "stack_index.parquet",
        source_locations,
        overview["minTime"],
        overview["maxTime"],
    )
    source_location_manifest = None
    source_location_output = output_root / "source_locations.parquet"
    if source_location_output.exists():
        source_location_manifest = {
            "path": "source_locations.parquet",
            "size": source_location_output.stat().st_size,
        }

    manifest = {
        "kind": "perfconverter.trace-manifest",
        "version": 1,
        "rootLabel": args.label or source_root.name,
        "baseUrl": ensure_trailing_slash(args.base_url),
        "sourceLocations": source_location_manifest,
        "stackIndex": stack_index,
        "streams": manifest_streams,
        "overview": overview,
        "profiles": {"capture": profile.to_profile(stack_timeline)},
    }

    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Wrote {manifest_path}")
    print(f"Wrote web trace dataset to {output_root}")


def discover_streams(root: Path) -> list[StreamInput]:
    streams: list[StreamInput] = []
    for path in sorted(root.rglob("*.parquet")):
        relative = path.relative_to(root).as_posix()
        if relative == "source_locations.parquet":
            continue
        match = TRACE_PATTERN.match(relative)
        if not match:
            continue
        pid_text, tid_text, event = match.groups()
        streams.append(StreamInput(path=path, pid=int(pid_text), tid=int(tid_text), event=event))
    return streams


def copy_source_locations(root: Path, output_root: Path, compression: str, row_group_size: int) -> None:
    source_path = root / "source_locations.parquet"
    if not source_path.exists():
        return
    table = pq.read_table(source_path, columns=SOURCE_LOCATION_COLUMNS)
    pq.write_table(
        table,
        output_root / "source_locations.parquet",
        compression=compression,
        row_group_size=row_group_size,
        write_statistics=True,
    )


def copy_stack_index(root: Path, output_root: Path, compression: str, row_group_size: int) -> dict | None:
    source_path = root / "stack_index.parquet"
    if not source_path.exists():
        return None

    table = pq.read_table(source_path).sort_by([("startTime", "ascending"), ("tid", "ascending"), ("depth", "ascending")])
    rows = table.num_rows
    if rows == 0:
        return None

    shard_root = output_root / "stack_index"
    shard_root.mkdir(parents=True, exist_ok=True)
    shard_count = min(256, max(16, math.ceil(rows / 150_000)))
    rows_per_shard = math.ceil(rows / shard_count)
    shards = []
    for shard_index, offset in enumerate(range(0, rows, rows_per_shard)):
        shard_table = table.slice(offset, rows_per_shard)
        starts = shard_table.column("startTime").to_numpy(zero_copy_only=False)
        ends = shard_table.column("endTime").to_numpy(zero_copy_only=False)
        output_path = shard_root / f"part-{shard_index:06d}.parquet"
        pq.write_table(
            shard_table,
            output_path,
            compression=compression,
            row_group_size=row_group_size,
            write_statistics=True,
        )
        relative = output_path.relative_to(output_root).as_posix()
        shards.append(
            {
                "path": relative,
                "size": output_path.stat().st_size,
                "rows": shard_table.num_rows,
                "minTime": int(starts.min()),
                "maxTime": int(ends.max()),
            }
        )

    return {
        "path": "stack_index",
        "size": sum(shard["size"] for shard in shards),
        "shards": shards,
    }


def load_source_locations(path: Path) -> dict[int, tuple[str, int, bool]]:
    if not path.exists():
        return {}
    table = pq.read_table(path, columns=SOURCE_LOCATION_COLUMNS)
    ids = table.column("id").to_pylist()
    dsos = table.column("dso").to_pylist()
    addresses = table.column("relativeAddress").to_pylist()
    kernels = table.column("isKernelIp").to_pylist()
    locations: dict[int, tuple[str, int, bool]] = {}
    for location_id, dso, address, kernel in zip(ids, dsos, addresses, kernels):
        locations[int(location_id)] = (decode_binary(dso) or "[address only]", int(address), bool(kernel))
    return locations


def build_stack_timeline(
    stack_index_path: Path,
    source_locations: dict[int, tuple[str, int, bool]],
    min_time: int,
    max_time: int,
) -> list[dict]:
    if not stack_index_path.exists():
        return []

    span = max(1, max_time - min_time + 1)
    bin_samples: list[Counter[tuple[str, int, bool]]] = [Counter() for _ in range(TIMELINE_BINS)]
    bin_cpus: list[defaultdict[tuple[str, int, bool], set[int]]] = [
        defaultdict(set) for _ in range(TIMELINE_BINS)
    ]
    parquet_file = pq.ParquetFile(stack_index_path)
    for batch in parquet_file.iter_batches(
        batch_size=512_000,
        columns=["startTime", "endTime", "cpu", "locationId"],
    ):
        table = pa.Table.from_batches([batch])
        starts = table.column("startTime").to_numpy(zero_copy_only=False)
        ends = table.column("endTime").to_numpy(zero_copy_only=False)
        cpus = table.column("cpu").to_numpy(zero_copy_only=False)
        location_ids = table.column("locationId").to_numpy(zero_copy_only=False)

        start_bins = np.floor(((starts.astype(np.float64) - min_time) / span) * TIMELINE_BINS).astype(np.int64)
        end_bins = np.floor(((ends.astype(np.float64) - min_time) / span) * TIMELINE_BINS).astype(np.int64)
        start_bins = np.clip(start_bins, 0, TIMELINE_BINS - 1)
        end_bins = np.clip(end_bins, 0, TIMELINE_BINS - 1)

        for start, end, cpu, location_id, start_bin, end_bin in zip(
            starts, ends, cpus, location_ids, start_bins, end_bins
        ):
            dso, address, is_kernel = source_locations.get(int(location_id), ("[address only]", 0, False))
            key = (dso, address, is_kernel)
            for bin_index in range(int(start_bin), int(end_bin) + 1):
                bin_start = min_time + (span * bin_index) / TIMELINE_BINS
                bin_end = min_time + (span * (bin_index + 1)) / TIMELINE_BINS
                overlap = min(int(end), bin_end) - max(int(start), bin_start)
                weight = max(1, int(overlap))
                bin_samples[bin_index][key] += weight
                bin_cpus[bin_index][key].add(int(cpu))

    bins = []
    for bin_index in range(TIMELINE_BINS):
        start = min_time + (span * bin_index) / TIMELINE_BINS
        end = min_time + (span * (bin_index + 1)) / TIMELINE_BINS
        frames = []
        for (dso, address, is_kernel), samples in bin_samples[bin_index].most_common(6):
            key = (dso, address, is_kernel)
            frames.append(
                {
                    "dso": dso,
                    "address": address,
                    "samples": samples,
                    "cpus": len(bin_cpus[bin_index].get(key, set())),
                    "isKernel": is_kernel,
                }
            )
        bins.append(
            {
                "bin": bin_index,
                "startTime": start,
                "endTime": end,
                "samples": sum(bin_samples[bin_index].values()),
                "frames": frames,
            }
        )

    return bins


def scan_stream_stats(stream: StreamInput, batch_size: int) -> None:
    parquet_file = pq.ParquetFile(stream.path)
    stream.rows = parquet_file.metadata.num_rows
    for batch in parquet_file.iter_batches(batch_size=batch_size, columns=["time", "cpu"]):
        table = pa.Table.from_batches([batch])
        times = table.column("time").to_numpy(zero_copy_only=False)
        cpus = table.column("cpu").to_numpy(zero_copy_only=False)
        stream.min_time = min_optional(stream.min_time, int(times.min()))
        stream.max_time = max_optional(stream.max_time, int(times.max()))
        stream.min_cpu = min_optional(stream.min_cpu, int(cpus.min()))
        stream.max_cpu = max_optional(stream.max_cpu, int(cpus.max()))


def export_stream(
    stream: StreamInput,
    source_root: Path,
    output_root: Path,
    profile: ProfileAccumulator,
    rows_per_shard: int,
    row_group_size: int,
    batch_size: int,
    sample_target: int,
    compression: str,
) -> list[dict]:
    parquet_file = pq.ParquetFile(stream.path)
    sample_stride = max(1, math.ceil(max(1, stream.rows) / max(1, sample_target)))
    shard_index = 0
    current: ShardWriter | None = None
    shards: list[dict] = []

    def open_shard(schema: pa.Schema) -> ShardWriter:
        nonlocal shard_index
        relative_path = f"pid={stream.pid}/tid={stream.tid}/event={stream.event}/part-{shard_index:06d}.parquet"
        path = output_root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        shard_index += 1
        return ShardWriter(
            writer=pq.ParquetWriter(path, schema, compression=compression, write_statistics=True),
            path=path,
            relative_path=relative_path,
        )

    for batch in parquet_file.iter_batches(batch_size=batch_size, columns=TRACE_COLUMNS):
        table = pa.Table.from_batches([batch]).select(TRACE_COLUMNS)
        offset = 0
        while offset < table.num_rows:
            if current is None:
                current = open_shard(table.schema)
            take = min(rows_per_shard - current.rows, table.num_rows - offset)
            part = table.slice(offset, take)
            current.write(part, row_group_size)
            profile.update(part, stream.event, sample_stride)
            offset += take
            if current.rows >= rows_per_shard:
                shards.append(current.close())
                current = None

    if current is not None:
        shards.append(current.close())

    return shards


def build_overview(streams: list[dict], has_source_locations: bool) -> dict:
    total_rows = sum(stream["rows"] for stream in streams)
    all_cpus_min = [stream["minCpu"] for stream in streams if stream["minCpu"] is not None]
    all_cpus_max = [stream["maxCpu"] for stream in streams if stream["maxCpu"] is not None]
    return {
        "totalRows": total_rows,
        "totalBytes": sum(sum(shard.get("size", 0) for shard in stream["shards"]) for stream in streams),
        "minTime": min(stream["minTime"] for stream in streams),
        "maxTime": max(stream["maxTime"] for stream in streams),
        "pids": sorted({stream["pid"] for stream in streams}),
        "tids": sorted({stream["tid"] for stream in streams}),
        "events": sorted({stream["event"] for stream in streams}),
        "cpuMin": min(all_cpus_min) if all_cpus_min else None,
        "cpuMax": max(all_cpus_max) if all_cpus_max else None,
        "hasSourceLocations": has_source_locations,
    }


def min_optional(current: int | None, value: int) -> int:
    return value if current is None else min(current, value)


def max_optional(current: int | None, value: int) -> int:
    return value if current is None else max(current, value)


def decode_binary(value: object) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return str(value)


def ensure_trailing_slash(value: str) -> str:
    return value if value.endswith("/") else f"{value}/"


def current_iso() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


if __name__ == "__main__":
    main()
