using PerfConverter.PerfStructs;
using Temp.Schema.Entry;

namespace PerfConverter;

public sealed unsafe class ResolvedLocation
{
    public uint Symoff { get; private init; }
    public ReadOnlyMemory<byte> Symbol { get; private init; }
    public ulong Address { get; private init; }
    public ulong SymbolStart { get; private init; }
    public ulong SymbolEnd { get; private init; }
    public ReadOnlyMemory<byte> Dso { get; private init; }
    public byte SymbolBinding { get; private init; }
    public byte Is64Bit { get; private init; }
    public byte IsKernelIp { get; private init; }
    public ReadOnlyMemory<byte> BuildId { get; private init; }
    public byte Filtered { get; private init; }
    public ReadOnlyMemory<byte> Comm { get; private init; }

    public static ResolvedLocation? From(PerfDlfilterAl* location)
    {
        if (location == null)
            return null;

        return new ResolvedLocation
        {
            Symoff = location->symoff,
            Symbol = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr(location->sym),
            Address = location->addr,
            SymbolStart = location->sym_start,
            SymbolEnd = location->sym_end,
            Dso = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr(location->dso),
            SymbolBinding = location->sym_binding,
            Is64Bit = location->is_64_bit,
            IsKernelIp = location->is_kernel_ip,
            BuildId = GetBuildId(location),
            Filtered = location->filtered,
            Comm = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr(location->comm)
        };
    }

    static ReadOnlyMemory<byte> GetBuildId(PerfDlfilterAl* location)
    {
        if (location->buildid == null || location->buildid_size <= 0)
            return ReadOnlyMemory<byte>.Empty;

        return EntryContentPool.Shared.GetByteMemory(new ReadOnlySpan<byte>(location->buildid, location->buildid_size));
    }
}
