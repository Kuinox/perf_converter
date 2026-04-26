using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;

namespace PerfConverter;

sealed class SegmentProcessor(
    IPersister<TraceEntry> tracePersister,
    IPersister<StackRange>? stackRangePersister)
{
    readonly Stack<ulong> _stackStarts = new();

    public unsafe void ProcessData(ulong entryId, PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address, string? srcFilePath, uint lineNumber)
    {
        var entry = TraceEntry.CreateFromPerf(sample, ip, address, entryId, srcFilePath, lineNumber);
        tracePersister.Persist(entry);
        if (stackRangePersister != null)
            ProcessStackTracking(entry, stackRangePersister);
    }

    void ProcessStackTracking(TraceEntry trace, IPersister<StackRange> stackRangePersister)
    {
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            _stackStarts.Push(trace.Id);
        }
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            var startTrace = _stackStarts.Count == 0 ? 0 : _stackStarts.Pop();
            var stackRange = new StackRange
            {
                StartTrace = startTrace,
                EndTrace = trace.Id
            };
            stackRangePersister.Persist(stackRange);
        }
    }
}
