using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
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
    readonly Stack<long> _stackStarts = new();
    
    // Reuse StringBuilder to avoid string allocation on key generation
    readonly StringBuilder _keyBuilder = new();

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
            // Use StringBuilder to avoid string concatenation allocations
            _keyBuilder.Clear();
            _keyBuilder.Append(sample->pid).Append('/').Append(sample->tid).Append("/segment").Append(_currentSegmentId).Append(".parquet");
            _currentTraceKey = _keyBuilder.ToString();
            
            _keyBuilder.Clear();
            _keyBuilder.Append(sample->pid).Append('/').Append(sample->tid).Append("/segment").Append(_currentSegmentId).Append("_stackranges.parquet");
            _currentStackRangeKey = _keyBuilder.ToString();
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
            _stackStarts.Push((long)trace.Id);
        }
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            var startTrace = _stackStarts.Count == 0 ? -1 : _stackStarts.Pop();
            var stackRange = new StackRange()
            {
                StartTrace = startTrace,
                EndTrace = (long)trace.Id
            };
            _stackRangePersister!.Persist(stackRange);
        }
    }

}
