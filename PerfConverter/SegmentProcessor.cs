using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;

namespace PerfConverter;

sealed class SegmentProcessor(
    uint pid,
    uint tid,
    int segmentId,
    string traceKey,
    string stackRangeKey,
    Func<string, IPersister<TraceEntry>> tracePersistenceFactory,
    Func<string, IPersister<StackRange>> stackRangePersistenceFactory) : IAsyncDisposable
{
    readonly IPersister<TraceEntry> _tracePersister = tracePersistenceFactory(traceKey);
    readonly IPersister<StackRange> _stackRangePersister = stackRangePersistenceFactory(stackRangeKey);
    readonly Stack<ulong> _stackStarts = new();

    ulong _currentEntryId = 0;

    public int SegmentId { get; } = segmentId;
    public uint Tid { get; } = tid;
    public uint Pid { get; } = pid;

    public unsafe void ProcessData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var entry = TraceEntry.CreateFromPerf(sample, ip, address, ++_currentEntryId);
        _tracePersister.Persist(entry);
        ProcessStackTracking(entry);
    }

    void ProcessStackTracking(TraceEntry trace)
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
            _stackRangePersister.Persist(stackRange);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _tracePersister.DisposeAsync();
        await _stackRangePersister.DisposeAsync();
    }
}
