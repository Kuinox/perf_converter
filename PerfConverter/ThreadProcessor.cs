using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using Temp.Schema.Entry;

namespace PerfConverter;

class ThreadProcessor : IAsyncDisposable
{
    readonly Func<string, IPersister<TraceEntry>> _tracePersistenceFactory;
    readonly Dictionary<string, SegmentProcessor> _eventMapping = [];
    readonly List<IPersister<TraceEntry>> _tracePersisters = [];
    readonly IPersister<StackRange> _stackPersister;

    ulong _currentEntryId = 1;

    public ThreadProcessor(uint pid,
                           uint tid,
                           Func<string, IPersister<TraceEntry>> tracePersistenceFactory,
                           Func<string, IPersister<StackRange>> stackRangePersistenceFactory)
    {
        _tracePersistenceFactory = tracePersistenceFactory;
        var key = $"pid={pid}/tid={tid}/branches_stackranges.parquet";
        _stackPersister = stackRangePersistenceFactory(key);
    }

    public unsafe void ProcessData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address, string srcFilePath, uint lineNumber)
    {
        var @event = EntryContentPool.Shared.GetStringFromUtf8Ptr(sample->@event);
        if (!_eventMapping.TryGetValue(@event, out var processor))
        {
            var eventName = @event.Split(':')[0];
            var traceKey = $"pid={sample->pid}/tid={sample->tid}/{eventName}.parquet";
            var tracePersister = _tracePersistenceFactory(traceKey);

            _tracePersisters.Add(tracePersister);

            processor = new SegmentProcessor(tracePersister, _stackPersister);
            _eventMapping[@event] = processor;
        }

        processor.ProcessData(_currentEntryId++, sample, ip, address, srcFilePath, lineNumber);
    }

    public async ValueTask DisposeAsync()
    {
        var task = _stackPersister.DisposeAsync().AsTask();
        var tasks = _tracePersisters.Select(x => x.DisposeAsync().AsTask());
        await Task.WhenAll(tasks);
        await task;
    }
}
