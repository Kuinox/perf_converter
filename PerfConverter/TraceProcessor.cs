using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using PerfConverter.Persistence;

namespace PerfConverter;

public unsafe class TraceProcessor(Func<string, ITracePersister> tracePersistenceFactory)
{
    readonly Dictionary<(uint, uint), ThreadProcessor> _processors = [];

    public void ProcessData(
        PerfDlFilterSample* sample,
        PerfDlfilterAl* ip,
        PerfDlfilterAl* address,
        ReadOnlyMemory<byte>? srcFileName,
        uint lineNumber)
    {
        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_processors, (sample->pid, sample->tid), out _);
        processor ??= new ThreadProcessor(tracePersistenceFactory);
        processor.ProcessData(sample, ip, address, srcFileName, lineNumber);
    }
}
