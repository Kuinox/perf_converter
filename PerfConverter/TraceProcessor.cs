using PerfConverter.Entry;
using System.Runtime.InteropServices;
using PerfConverter.Persistence;

namespace PerfConverter;

public unsafe class TraceProcessor(Func<string, ITracePersister> tracePersistenceFactory)
{
    readonly Dictionary<(uint, uint), ThreadProcessor> _processors = [];

    public void ProcessData(
        OwnedPerfSample sample,
        ResolvedLocation ip,
        ResolvedLocation? address,
        ulong ipLocationId,
        ulong addressLocationId,
        ReadOnlyMemory<byte>? srcFileName,
        uint lineNumber)
    {
        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_processors, (sample.Pid, sample.Tid), out _);
        processor ??= new ThreadProcessor(tracePersistenceFactory);
        processor.ProcessData(sample, ip, address, ipLocationId, addressLocationId, srcFileName, lineNumber);
    }
}
