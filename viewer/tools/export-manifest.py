#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Create a PerfConverter trace-manifest.json for static remote Parquet hosting."
    )
    parser.add_argument("parquet_output", type=Path, help="Path to the parquet_output folder")
    parser.add_argument(
        "--base-url",
        required=True,
        help="Public URL prefix where the parquet_output contents are hosted",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("public/trace-manifest.json"),
        help="Manifest output path",
    )
    parser.add_argument(
        "--label",
        default=None,
        help="Human-readable trace label shown in the viewer",
    )
    args = parser.parse_args()

    root = args.parquet_output.resolve()
    if not root.exists():
        raise SystemExit(f"Parquet output folder does not exist: {root}")

    files = []
    source_locations = None
    for path in sorted(root.rglob("*.parquet")):
        relative = path.relative_to(root).as_posix()
        entry = {"path": relative, "size": path.stat().st_size}
        if relative == "source_locations.parquet":
            source_locations = entry
        elif "/pid=" in f"/{relative}" and "/tid=" in f"/{relative}":
            files.append(entry)

    manifest = {
        "kind": "perfconverter.trace-manifest",
        "version": 1,
        "rootLabel": args.label or root.name,
        "baseUrl": ensure_trailing_slash(args.base_url),
        "sourceLocations": source_locations,
        "files": files,
    }

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Wrote {args.out} with {len(files)} trace files")


def ensure_trailing_slash(value: str) -> str:
    return value if value.endswith("/") else f"{value}/"


if __name__ == "__main__":
    main()
