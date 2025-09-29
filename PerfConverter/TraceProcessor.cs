using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using PerfConverter.Persistence;

namespace PerfConverter;

public unsafe class TraceProcessor(Func<string, IPersister<TraceEntry>> tracePersistenceFactory, Func<string, IPersister<StackRange>> stackRangePersistenceFactory)
{
    readonly Dictionary<(uint, uint), ThreadProcessor> _processors = [];

    public void ProcessData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_processors, (sample->pid, sample->tid), out _);
        processor ??= new ThreadProcessor(sample->pid, sample->tid, tracePersistenceFactory, stackRangePersistenceFactory);
        processor.ProcessData(sample, ip, address);
    }
}
