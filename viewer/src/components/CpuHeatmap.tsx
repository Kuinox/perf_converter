import { max, scaleSequentialSqrt } from "d3";
import { interpolateMagma } from "d3-scale-chromatic";
import type { CpuBin } from "../data/types";
import { formatCompact } from "../format";

interface CpuHeatmapProps {
  bins: CpuBin[];
}

export function CpuHeatmap({ bins }: CpuHeatmapProps) {
  if (!bins.length) {
    return <div className="empty-state">No CPU activity bins for this selection.</div>;
  }

  const cpus = Array.from(new Set(bins.map((bin) => bin.cpu))).sort((left, right) => left - right);
  const binCount = Math.max(1, max(bins, (bin) => bin.bin) ?? 0) + 1;
  const maxSamples = Math.max(1, max(bins, (bin) => bin.samples) ?? 1);
  const color = scaleSequentialSqrt(interpolateMagma).domain([0, maxSamples]);
  const lookup = new Map(bins.map((bin) => [`${bin.cpu}:${bin.bin}`, bin.samples]));

  return (
    <div className="cpu-heatmap" style={{ gridTemplateRows: `repeat(${cpus.length}, 18px)` }}>
      {cpus.map((cpu) => (
        <div className="cpu-row" key={cpu}>
          <div className="cpu-label">CPU {cpu}</div>
          <div className="cpu-cells" style={{ gridTemplateColumns: `repeat(${binCount}, minmax(3px, 1fr))` }}>
            {Array.from({ length: binCount }, (_, bin) => {
              const samples = lookup.get(`${cpu}:${bin}`) ?? 0;
              return (
                <div
                  className="cpu-cell"
                  key={bin}
                  title={`CPU ${cpu}, bin ${bin}: ${formatCompact(samples)} samples`}
                  style={{
                    background: samples > 0 ? color(samples) : "rgba(255,255,255,0.035)"
                  }}
                />
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
}
