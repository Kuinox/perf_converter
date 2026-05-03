import type { ModuleHotspot } from "../data/types";
import { dsoLabel, formatCompact, formatHex, percent, shortPath } from "../format";

interface ModuleBarsProps {
  modules: ModuleHotspot[];
}

export function ModuleBars({ modules }: ModuleBarsProps) {
  if (!modules.length) {
    return <div className="empty-state">Module hotspots are unavailable for this trace.</div>;
  }

  const maxSamples = Math.max(...modules.map((module) => module.samples), 1);
  const total = modules.reduce((sum, module) => sum + module.samples, 0);

  return (
    <div className="module-list">
      {modules.map((module, index) => (
        <div className="module-row" key={`${module.dso}:${index}`}>
          <div className="module-rank">{String(index + 1).padStart(2, "0")}</div>
          <div className="module-main">
            <div className="module-heading">
              <span title={module.dso}>{dsoLabel(module.dso)}</span>
              <strong>{formatCompact(module.samples)}</strong>
            </div>
            <div className="module-path" title={module.dso}>
              {shortPath(module.dso, 84)}
            </div>
            <div className="module-meter">
              <div
                className="module-meter-fill"
                style={{ width: `${Math.max(2, (module.samples / maxSamples) * 100)}%` }}
              />
            </div>
            <div className="module-meta">
              <span>{percent(module.samples, total)} of shown samples</span>
              <span>{formatCompact(module.addresses)} addresses</span>
              <span>
                {formatHex(module.minAddress)}..{formatHex(module.maxAddress)}
              </span>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
