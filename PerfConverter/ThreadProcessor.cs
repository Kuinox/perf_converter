using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using System.Text;

namespace PerfConverter;

unsafe class ThreadProcessor(uint tid, uint pid, IEnumerable<ulong> auxDrop, Func<string, IPersister<TraceEntry>> tracePersistenceFactory, Func<string, IPersister<StackRange>> stackRangePersistenceFactory)
{
    readonly Queue<ulong> auxDrop = new(auxDrop.Order());
    int _currentSegmentId = 0;
    ulong _currentEntryId = 0;
    string _currentTraceKey = null!;
    string _currentStackRangeKey = null!;
    IPersister<TraceEntry>? _tracePersister;
    IPersister<StackRange>? _stackRangePersister;
    readonly Stack<ulong> _stackStarts = new();

    public uint Tid { get; } = tid;
    public uint Pid { get; } = pid;


    public void QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var isNewTrace = auxDrop.TryPeek(out var newTraceTime) && newTraceTime < sample->time;
        if (isNewTrace)
        {
            _tracePersister?.DisposeAsync().AsTask();
            _stackRangePersister?.DisposeAsync().AsTask();
            auxDrop.Dequeue();
            _currentSegmentId++;
            _currentTraceKey = $"{sample->pid}/{sample->tid}/segment{_currentSegmentId}.parquet";
            _currentStackRangeKey = $"{sample->pid}/{sample->tid}/segment{_currentSegmentId}_stackranges.parquet";
            _currentEntryId=0;
            _tracePersister = tracePersistenceFactory(_currentTraceKey);
            _stackRangePersister = stackRangePersistenceFactory(_currentStackRangeKey);
            _stackStarts.Clear();
        }

        var entry = TraceEntry.CreateFromPerf(sample, ip, address, ++_currentEntryId);
        _tracePersister!.Persist(entry);
        
        // Process stack tracking for call/return pairs
        ProcessStackTracking(entry);
    }

    // BuildEntry method removed - now handled by TraceEntry.CreateFromPerf

    private void ProcessStackTracking(TraceEntry trace)
    {
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            _stackStarts.Push(trace.Id);
        }
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            var startTrace = _stackStarts.Count == 0 ? 0 : _stackStarts.Pop();
            var stackRange = new StackRange()
            {
                StartTrace = startTrace,
                EndTrace = trace.Id
            };
            _stackRangePersister!.Persist(stackRange);
        }
    }

}
