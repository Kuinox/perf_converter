using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using System.Text.Json;
using PerfConverter.Persistence;

namespace PerfConverter;

public unsafe class TraceProcessor
{
    readonly IReadOnlyDictionary<(uint, uint), ulong[]> _auxDataLoss;
    readonly Dictionary<(uint, uint), ThreadProcessor> _processors = [];
    readonly Func<string, IPersister<TraceEntry>> _tracePersistenceFactory;
    readonly Func<string, IPersister<StackRange>> _stackRangePersistenceFactory;

    public TraceProcessor(Func<string, IPersister<TraceEntry>> tracePersistenceFactory, Func<string, IPersister<StackRange>> stackRangePersistenceFactory)
    {
        _tracePersistenceFactory = tracePersistenceFactory;
        _stackRangePersistenceFactory = stackRangePersistenceFactory;
        var auxDataLossJson = Environment.GetEnvironmentVariable("AUX_DATA_LOSS");
        
        // Optimize by avoiding deserialization if no aux data loss info
        if (string.IsNullOrEmpty(auxDataLossJson))
        {
            _auxDataLoss = new Dictionary<(uint, uint), ulong[]>();
        }
        else
        {
            var auxDataLoss = JsonSerializer.Deserialize(auxDataLossJson, SourceGenerationContext.Default.AuxDataLostArray)!;
            _auxDataLoss = auxDataLoss
                .GroupBy(x => (x.Pid, x.Tid))
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(x => x.Time).Append(0uL).ToArray());
        }
    }

    public void QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {

        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_processors, (sample->pid, sample->tid), out _);
        if (processor is null)
        {
            var auxDataLoss = _auxDataLoss.GetValueOrDefault((sample->pid, sample->tid), [0]);
            processor = new ThreadProcessor(sample->tid, sample->pid, auxDataLoss, _tracePersistenceFactory, _stackRangePersistenceFactory);
        }
        
        processor.QueueData(sample, ip, address);
    }
}
