using Temp.Schema.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetTracePersistence(TraceSampleRowSchema.PipelineWriter writer) : ITracePersister
{
    bool _disposed;

    public void Persist(
        ulong entryId,
        OwnedPerfSample sample,
        ResolvedLocation ip,
        ResolvedLocation? address,
        ulong ipLocationId,
        ulong addressLocationId,
        ReadOnlyMemory<byte>? srcFilePath,
        uint lineNumber,
        ReadOnlyMemory<byte> eventName)
    {
        var row = writer.GetRow();
        row.Id = entryId;
        row.PerfId = sample.Id;
        row.Pid = sample.Pid;
        row.Tid = sample.Tid;
        row.Time = sample.Time;
        row.Cpu = (uint)sample.Cpu;
        row.Flags = sample.Flags;
        row.Ip = ip.Address;
        row.IpLocationId = ipLocationId;
        row.Addr = address?.Address ?? 0UL;
        row.AddressLocationId = addressLocationId;
        row.Period = sample.Period;
        row.InsnCnt = sample.InsnCount;
        row.CycCnt = sample.CycleCount;
        row.Weight = sample.Weight;
        row.Cpumode = sample.CpuMode;
        row.AddrCorrelatesSym = sample.AddressCorrelatesSymbol;
        row.Event = eventName;
        row.MachinePid = (uint)sample.MachinePid;
        row.Vcpu = (uint)sample.Vcpu;
        row.SourceFileName = srcFilePath;
        row.SourceLineNumber = lineNumber;

        row.IpSymoff = ip.Symoff;
        row.IpSym = ip.Symbol.IsEmpty ? null : ip.Symbol;
        row.IpSymStart = ip.SymbolStart;
        row.IpSymEnd = ip.SymbolEnd;
        row.IpDso = ip.Dso;
        row.IpSymBinding = ip.SymbolBinding;
        row.IpIs64Bit = ip.Is64Bit;
        row.IpIsKernelIp = ip.IsKernelIp;
        row.IpBuildId = ip.BuildId;
        row.IpFiltered = ip.Filtered;
        row.IpComm = ip.Comm.IsEmpty ? null : ip.Comm;

        if (address == null)
        {
            row.HaveAddress = false;
            row.AddressSymoff = 0;
            row.AddressSym = null;
            row.AddressSymStart = 0;
            row.AddressSymEnd = 0;
            row.AddressDso = null;
            row.AddressSymBinding = 0;
            row.AddressIs64Bit = 0;
            row.AddressIsKernelIp = 0;
            row.AddressBuildId = null;
            row.AddressFiltered = 0;
            row.AddressComm = null;
        }
        else
        {
            row.HaveAddress = true;
            row.AddressSymoff = address.Symoff;
            row.AddressSym = address.Symbol.IsEmpty ? null : address.Symbol;
            row.AddressSymStart = address.SymbolStart;
            row.AddressSymEnd = address.SymbolEnd;
            row.AddressDso = address.Dso;
            row.AddressSymBinding = address.SymbolBinding;
            row.AddressIs64Bit = address.Is64Bit;
            row.AddressIsKernelIp = address.IsKernelIp;
            row.AddressBuildId = address.BuildId;
            row.AddressFiltered = address.Filtered;
            row.AddressComm = address.Comm.IsEmpty ? null : address.Comm;
        }

        writer.Next();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            writer.Complete();
            _disposed = true;
        }
    }

    public static ITracePersister Create(string filePath)
        => Create(filePath, onFlush: null);

    public static ITracePersister Create(string filePath, Action<int>? onFlush)
    {
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = TraceSampleRowSchema.CreateRowWriter(fileStream, onFlush, ParquetPersistenceOptions.WriterOptions);

        return new ParquetTracePersistence(writer);
    }
}
