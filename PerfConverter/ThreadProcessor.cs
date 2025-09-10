using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;

namespace PerfConverter;

class ThreadProcessor(uint tid, uint pid, IEnumerable<ulong> auxDrop, Func<string, IPersister<TraceEntry>> tracePersistenceFactory, Func<string, IPersister<StackRange>> stackRangePersistenceFactory) : IAsyncDisposable
{
    readonly Queue<ulong> _auxDrop = new(auxDrop.Order());
    readonly List<string> _threadNames = [];
    int _currentSegmentId = 0;
    SegmentProcessor? _segment;
    readonly Func<string, IPersister<TraceEntry>> _tracePersistenceFactory = tracePersistenceFactory;
    readonly Func<string, IPersister<StackRange>> _stackRangePersistenceFactory = stackRangePersistenceFactory;

    public uint Tid { get; } = tid;
    public uint Pid { get; } = pid;

    ulong _currentEntryId = 1;

    public unsafe void ProcessData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var isNewSegment = _auxDrop.TryPeek(out var newTraceTime) && newTraceTime < sample->time;
        if (isNewSegment)
        {
            _segment?.DisposeAsync().AsTask();
            _auxDrop.Dequeue();
            _currentSegmentId++;
            var traceKey = $"pid={sample->pid}/tid={sample->tid}/segment{_currentSegmentId}.parquet";
            var stackRangeKey = $"pid={sample->pid}/tid={sample->tid}/segment{_currentSegmentId}_stackranges.parquet";
            _segment = new SegmentProcessor(Pid, Tid, _currentSegmentId, traceKey, stackRangeKey, _tracePersistenceFactory, _stackRangePersistenceFactory);
        }

        _segment!.ProcessData(_currentEntryId++, sample, ip, address);
    }

    public async ValueTask DisposeAsync()
    {
        if (_segment is not null)
            await _segment.DisposeAsync();
    }
}
