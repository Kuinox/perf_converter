import type { BranchEdge } from "../data/types";
import { dsoLabel, formatCompact, formatHex } from "../format";

interface BranchFlowsProps {
  edges: BranchEdge[];
}

export function BranchFlows({ edges }: BranchFlowsProps) {
  if (!edges.length) {
    return <div className="empty-state">Branch edge data is shown when a branches event is selected.</div>;
  }

  const maxSamples = Math.max(...edges.map((edge) => edge.samples), 1);

  return (
    <div className="branch-list">
      {edges.slice(0, 32).map((edge, index) => (
        <div className="branch-row" key={`${edge.fromDso}:${edge.fromAddress}:${edge.toDso}:${edge.toAddress}:${index}`}>
          <div className="branch-count">
            <strong>{formatCompact(edge.samples)}</strong>
            <span>{formatCompact(edge.cpus)} CPUs</span>
          </div>
          <div className="branch-flow">
            <div className="branch-node">
              <span>{dsoLabel(edge.fromDso)}</span>
              <code>{formatHex(edge.fromAddress)}</code>
            </div>
            <div className="branch-line">
              <div
                className="branch-line-fill"
                style={{ width: `${Math.max(8, (edge.samples / maxSamples) * 100)}%` }}
              />
            </div>
            <div className="branch-node branch-node-target">
              <span>{dsoLabel(edge.toDso)}</span>
              <code>{formatHex(edge.toAddress)}</code>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
