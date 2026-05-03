import { GitBranch, Layers } from "lucide-react";
import type { TraceRow } from "../data/types";
import { dsoLabel, formatHex, formatInteger } from "../format";

interface StackTracePanelProps {
  rows: TraceRow[];
}

export function StackTracePanel({ rows }: StackTracePanelProps) {
  const branchRows = rows.filter((row) => row.toAddress !== null).slice(-48).reverse();

  if (!rows.length) {
    return (
      <div className="stack-empty">
        <Layers size={26} />
        <strong>Stack trace appears after selecting a timeline range.</strong>
        <span>True call stacks require callchain data in the converted output.</span>
      </div>
    );
  }

  if (!branchRows.length) {
    return (
      <div className="stack-empty">
        <Layers size={26} />
        <strong>No branch stack in this range.</strong>
        <span>The selected rows do not include branch targets.</span>
      </div>
    );
  }

  return (
    <div className="stack-trace">
      <div className="stack-note">
        <GitBranch size={14} />
        <span>Execution trace from branch targets in the selected range.</span>
      </div>
      <ol>
        {branchRows.map((row, index) => (
          <li key={`${row.id}:${row.time}:${row.ip}:${index}`}>
            <div className="stack-frame-depth">{index}</div>
            <div className="stack-frame-main">
              <strong>{dsoLabel(row.toDso)}</strong>
              <code>{formatHex(row.toAddress)}</code>
              <span>
                from {dsoLabel(row.dso)} {formatHex(row.relativeAddress ?? row.ip)}
              </span>
            </div>
            <div className="stack-frame-meta">
              <span>{row.event}</span>
              <span>cpu {row.cpu}</span>
              <span>{formatInteger(row.time)}</span>
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}
