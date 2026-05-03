import { max, scaleLinear, scaleOrdinal } from "d3";
import type { AddressHotspot } from "../data/types";
import { dsoLabel, formatCompact, formatHex } from "../format";
import { useElementSize } from "../useResizeObserver";

interface AddressMapProps {
  addresses: AddressHotspot[];
}

const palette = ["#14b8a6", "#f59e0b", "#f43f5e", "#84cc16", "#38bdf8", "#f97316", "#eab308"];

export function AddressMap({ addresses }: AddressMapProps) {
  const { ref, size } = useElementSize<HTMLDivElement>();

  if (!addresses.length) {
    return <div className="empty-state">No resolved hot addresses for this selection.</div>;
  }

  const grouped = Array.from(groupBy(addresses, (entry) => entry.dso).entries())
    .map(([dso, values]) => ({
      dso,
      values: values.sort((left, right) => left.relativeAddress - right.relativeAddress),
      total: values.reduce((sum, entry) => sum + entry.samples, 0)
    }))
    .sort((left, right) => right.total - left.total)
    .slice(0, 8);

  const width = Math.max(420, size.width || 720);
  const rowHeight = 54;
  const height = grouped.length * rowHeight + 24;
  const maxSamples = Math.max(1, max(addresses, (entry) => entry.samples) ?? 1);
  const radius = scaleLinear([0, Math.sqrt(maxSamples)], [3, 15]);
  const color = scaleOrdinal<string, string>().domain(grouped.map((group) => group.dso)).range(palette);

  return (
    <div className="address-map" ref={ref}>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Hot address map">
        {grouped.map((group, rowIndex) => {
          const y = 22 + rowIndex * rowHeight;
          const minAddress = Math.min(...group.values.map((entry) => entry.relativeAddress));
          const maxAddress = Math.max(...group.values.map((entry) => entry.relativeAddress));
          const x = scaleLinear([minAddress, maxAddress || minAddress + 1], [170, width - 28]);

          return (
            <g key={group.dso}>
              <text x="12" y={y + 5} className="address-label">
                {dsoLabel(group.dso)}
              </text>
              <line x1="170" x2={width - 28} y1={y} y2={y} className="address-axis" />
              {group.values.slice(0, 24).map((entry) => (
                <circle
                  key={`${group.dso}:${entry.relativeAddress}`}
                  cx={x(entry.relativeAddress)}
                  cy={y}
                  r={radius(Math.sqrt(entry.samples))}
                  fill={entry.isKernel ? "#f43f5e" : color(group.dso)}
                  opacity="0.78"
                >
                  <title>
                    {dsoLabel(entry.dso)} {formatHex(entry.relativeAddress)}: {formatCompact(entry.samples)} samples
                  </title>
                </circle>
              ))}
              <text x="170" y={y + 24} className="address-range">
                {formatHex(minAddress)}
              </text>
              <text x={width - 28} y={y + 24} textAnchor="end" className="address-range">
                {formatHex(maxAddress)}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function groupBy<T, TKey>(values: T[], keySelector: (value: T) => TKey): Map<TKey, T[]> {
  const map = new Map<TKey, T[]>();
  for (const value of values) {
    const key = keySelector(value);
    const existing = map.get(key);
    if (existing) {
      existing.push(value);
    } else {
      map.set(key, [value]);
    }
  }

  return map;
}
