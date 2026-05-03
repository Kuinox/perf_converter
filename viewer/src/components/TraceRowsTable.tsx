import type { TraceRow } from "../data/types";
import { dsoLabel, formatHex, formatInteger } from "../format";

interface TraceRowsTableProps {
  rows: TraceRow[];
  loading: boolean;
}

export function TraceRowsTable({ rows, loading }: TraceRowsTableProps) {
  if (loading) {
    return <div className="empty-state">Querying detail rows with DuckDB...</div>;
  }

  if (!rows.length) {
    return <div className="empty-state">Select a timeline range to inspect instruction or branch rows.</div>;
  }

  return (
    <div className="trace-row-table">
      <table>
        <thead>
          <tr>
            <th>Trace</th>
            <th>CPU</th>
            <th>Time</th>
            <th>IP</th>
            <th>Module</th>
            <th>Target</th>
            <th>Insn</th>
            <th>Cycles</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={`${row.id}:${row.time}:${row.ip}`}>
              <td>{formatInteger(row.id)}</td>
              <td>{row.cpu}</td>
              <td>{formatInteger(row.time)}</td>
              <td>
                <code>{formatHex(row.relativeAddress ?? row.ip)}</code>
              </td>
              <td title={row.dso}>{dsoLabel(row.dso)}</td>
              <td title={row.toDso}>
                {row.toAddress !== null ? (
                  <>
                    <span>{dsoLabel(row.toDso)}</span> <code>{formatHex(row.toAddress)}</code>
                  </>
                ) : (
                  <span className="muted">-</span>
                )}
              </td>
              <td>{formatInteger(row.instructions)}</td>
              <td>{formatInteger(row.cycles)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
