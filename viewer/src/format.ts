const numberFormat = new Intl.NumberFormat("en-US");
const compactFormat = new Intl.NumberFormat("en-US", {
  notation: "compact",
  maximumFractionDigits: 2
});

export function formatInteger(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "-";
  }

  return numberFormat.format(Math.round(value));
}

export function formatCompact(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "-";
  }

  return compactFormat.format(value);
}

export function formatDurationNs(ns: number): string {
  if (!Number.isFinite(ns) || ns < 0) {
    return "-";
  }

  if (ns < 1_000) return `${ns.toFixed(0)} ns`;
  if (ns < 1_000_000) return `${(ns / 1_000).toFixed(2)} us`;
  if (ns < 1_000_000_000) return `${(ns / 1_000_000).toFixed(2)} ms`;
  if (ns < 60_000_000_000) return `${(ns / 1_000_000_000).toFixed(2)} s`;
  return `${(ns / 60_000_000_000).toFixed(2)} min`;
}

export function formatTimeOffsetNs(time: number, origin: number): string {
  if (!Number.isFinite(time) || !Number.isFinite(origin)) {
    return "-";
  }

  const delta = time - origin;
  if (delta === 0) {
    return "0 ns";
  }

  const sign = delta < 0 ? "-" : "+";
  return `${sign}${formatDurationNs(Math.abs(delta))}`;
}

export function formatHex(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "0x0";
  }

  return `0x${Math.max(0, Math.trunc(value)).toString(16)}`;
}

export function shortPath(path: string, max = 52): string {
  if (path.length <= max) {
    return path;
  }

  const file = path.split(/[\\/]/).pop() ?? path;
  const headLength = Math.max(8, max - file.length - 4);
  return `${path.slice(0, headLength)}...${file}`;
}

export function basename(path: string): string {
  const parts = path.split(/[\\/]/).filter(Boolean);
  return parts.at(-1) ?? path;
}

export function dsoLabel(dso: string): string {
  if (!dso) {
    return "[address only]";
  }

  if (dso.startsWith("[")) {
    return dso;
  }

  return basename(dso);
}

export function percent(value: number, total: number): string {
  if (!Number.isFinite(value) || !Number.isFinite(total) || total <= 0) {
    return "0%";
  }

  return `${((value / total) * 100).toFixed(value / total >= 0.1 ? 0 : 1)}%`;
}
