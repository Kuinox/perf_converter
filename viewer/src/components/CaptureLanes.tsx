import { Activity, GitBranch } from "lucide-react";
import type { TraceFileSummary } from "../data/types";
import { formatCompact, formatDurationNs } from "../format";

interface CaptureLanesProps {
  streams: TraceFileSummary[];
}

export function CaptureLanes({ streams }: CaptureLanesProps) {
  if (!streams.length) {
    return <div className="empty-state">No capture streams were found.</div>;
  }

  const knownRows = streams.filter((stream) => stream.rowsKnown);
  const maxRows = Math.max(...knownRows.map((stream) => stream.rows), 1);
  const grouped = groupByThread(streams);

  return (
    <div className="capture-lanes">
      {grouped.map((group) => (
        <section className="capture-thread" key={`${group.pid}:${group.tid}`}>
          <div className="capture-thread-label">
            <strong>pid {group.pid}</strong>
            <span>tid {group.tid}</span>
          </div>
          <div className="capture-thread-streams">
            {group.streams.map((stream) => {
              const isBranch = stream.event.toLowerCase().includes("branch");
              const width = stream.rowsKnown ? Math.max(2, (stream.rows / maxRows) * 100) : 18;
              return (
                <div className="capture-stream" key={stream.id}>
                  <div className="capture-stream-icon">
                    {isBranch ? <GitBranch size={14} /> : <Activity size={14} />}
                  </div>
                  <div className="capture-stream-main">
                    <div className="capture-stream-heading">
                      <span>{stream.event}</span>
                      <strong>
                        {stream.rowsKnown ? formatCompact(stream.rows) : "indexed"}
                      </strong>
                    </div>
                    <div className="capture-stream-meter">
                      <div
                        className={isBranch ? "branch" : "instruction"}
                        style={{ width: `${width}%` }}
                      />
                    </div>
                  </div>
                  <div className="capture-stream-time">
                    {stream.timeKnown ? formatDurationNs(stream.maxTime - stream.minTime) : "ready"}
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      ))}
    </div>
  );
}

function groupByThread(streams: TraceFileSummary[]) {
  const groups = new Map<string, { pid: number; tid: number; streams: TraceFileSummary[] }>();
  for (const stream of streams) {
    const key = `${stream.pid}:${stream.tid}`;
    const group = groups.get(key);
    if (group) {
      group.streams.push(stream);
    } else {
      groups.set(key, { pid: stream.pid, tid: stream.tid, streams: [stream] });
    }
  }

  return Array.from(groups.values())
    .map((group) => ({
      ...group,
      streams: group.streams.sort((left, right) => left.event.localeCompare(right.event))
    }))
    .sort((left, right) => left.pid - right.pid || left.tid - right.tid);
}
