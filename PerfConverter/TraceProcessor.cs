using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using System.Text.Json;
using Temp.Core;

namespace PerfConverter;

public unsafe class TraceProcessor
{
    readonly IReadOnlyDictionary<(uint, uint), ulong[]> _auxDataLoss;
    readonly Dictionary<(uint, uint), ThreadProcessor> _processors = [];
    readonly Func<string, IPersister<TraceEntry>> _persistenceFactory;

    public TraceProcessor(Func<string, IPersister<TraceEntry>> persistenceFactory)
    {
        _persistenceFactory = persistenceFactory;
        var auxDataLossJson = Environment.GetEnvironmentVariable("AUX_DATA_LOSS")!;
        var auxDataLoss = JsonSerializer.Deserialize(auxDataLossJson, SourceGenerationContext.Default.AuxDataLostArray)!;
        _auxDataLoss = auxDataLoss
            .GroupBy(x => (x.Pid, x.Tid))
            .ToDictionary(
                x => x.Key,
                x => x.Select(x => x.Time).Append(0uL).ToArray()); ;
    }

    public void QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var auxDataLoss = _auxDataLoss[(sample->pid, sample->tid)];

        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_processors, (sample->pid, sample->tid), out _);
        processor ??= new ThreadProcessor(sample->tid, sample->pid, auxDataLoss, _persistenceFactory);

        processor.QueueData(sample, ip, address);
    }
}
