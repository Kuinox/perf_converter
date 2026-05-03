import { max, scaleSequentialSqrt } from "d3";
import { interpolateTurbo } from "d3-scale-chromatic";
import type { AddressHotspot } from "../data/types";
import { dsoLabel, formatCompact, formatHex } from "../format";
import { useElementSize } from "../useResizeObserver";

interface AddressMapProps {
  addresses: AddressHotspot[];
}

interface HeatRow {
  dso: string;
  total: number;
  minAddress: number;
  maxAddress: number;
  bins: HeatBin[];
}

interface HeatBin {
  index: number;
  start: number;
  end: number;
  samples: number;
  kernelSamples: number;
}

const binCount = 80;

export function AddressMap({ addresses }: AddressMapProps) {
  const { ref, size } = useElementSize<HTMLDivElement>();

  if (!addresses.length) {
    return <div className="empty-state">No resolved hot addresses for this selection.</div>;
  }

  const rows = buildRows(addresses).slice(0, 12);
  const width = Math.max(520, size.width || 860);
  const labelWidth = width < 720 ? 136 : 190;
  const heatLeft = labelWidth + 12;
  const heatRight = width - 20;
  const heatWidth = Math.max(220, heatRight - heatLeft);
  const cellGap = 1;
  const cellWidth = Math.max(2, heatWidth / binCount - cellGap);
  const rowHeight = 34;
  const height = rows.length * rowHeight + 38;
  const maxSamples = Math.max(1, max(rows.flatMap((row) => row.bins), (bin) => bin.samples) ?? 1);
  const color = scaleSequentialSqrt(interpolateTurbo).domain([0, maxSamples]);

  return (
    <div className="address-map address-heatmap" ref={ref}>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Hot address heatmap">
        {rows.map((row, rowIndex) => {
          const y = 18 + rowIndex * rowHeight;
          return (
            <g key={row.dso}>
              <text x="12" y={y + 13} className="address-label">
                {dsoLabel(row.dso)}
              </text>
              <text x={labelWidth} y={y + 13} textAnchor="end" className="address-range">
                {formatCompact(row.total)}
              </text>
              {row.bins.map((bin) => {
                const x = heatLeft + bin.index * (cellWidth + cellGap);
                return (
                  <rect
                    className="address-heat-cell"
                    key={`${row.dso}:${bin.index}`}
                    x={x}
                    y={y}
                    width={cellWidth}
                    height={20}
                    fill={bin.samples > 0 ? color(bin.samples) : "rgba(255,255,255,0.035)"}
                    opacity={bin.kernelSamples ? 1 : 0.86}
                  >
                    <title>
                      {dsoLabel(row.dso)} {formatHex(bin.start)} - {formatHex(bin.end)}:{" "}
                      {formatCompact(bin.samples)} samples
                    </title>
                  </rect>
                );
              })}
              <text x={heatLeft} y={y + 31} className="address-range">
                {formatHex(row.minAddress)}
              </text>
              <text x={heatRight} y={y + 31} textAnchor="end" className="address-range">
                {formatHex(row.maxAddress)}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function buildRows(addresses: AddressHotspot[]): HeatRow[] {
  return Array.from(groupBy(addresses, (entry) => entry.dso).entries())
    .map(([dso, values]) => {
      const minAddress = Math.min(...values.map((entry) => entry.relativeAddress));
      const maxAddress = Math.max(...values.map((entry) => entry.relativeAddress));
      const span = Math.max(1, maxAddress - minAddress + 1);
      const bins: HeatBin[] = Array.from({ length: binCount }, (_, index) => ({
        index,
        start: minAddress + Math.floor((span * index) / binCount),
        end: minAddress + Math.floor((span * (index + 1)) / binCount),
        samples: 0,
        kernelSamples: 0
      }));

      for (const entry of values) {
        const index = Math.min(
          binCount - 1,
          Math.max(0, Math.floor(((entry.relativeAddress - minAddress) / span) * binCount))
        );
        bins[index].samples += entry.samples;
        if (entry.isKernel) {
          bins[index].kernelSamples += entry.samples;
        }
      }

      return {
        dso,
        total: values.reduce((sum, entry) => sum + entry.samples, 0),
        minAddress,
        maxAddress,
        bins
      };
    })
    .sort((left, right) => right.total - left.total);
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
