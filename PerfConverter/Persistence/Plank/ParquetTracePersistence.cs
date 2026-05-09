using PerfConverter.PerfStructs;
using Temp.Schema.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetTracePersistence(TraceSampleRowSchema.PipelineWriter writer) : ITracePersister
{
    bool _disposed;

    public unsafe void Persist(
        ulong entryId,
        PerfDlFilterSample* sample,
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
        row.PerfId = sample->id;
        row.Pid = sample->pid;
        row.Tid = sample->tid;
        row.Time = sample->time;
        row.Cpu = (uint)sample->cpu;
        row.Flags = sample->flags;
        row.Ip = ip.Address;
        row.IpLocationId = ipLocationId;
        row.Addr = address?.Address ?? 0UL;
        row.AddressLocationId = addressLocationId;
        row.Period = sample->period;
        row.InsnCnt = sample->insn_cnt;
        row.CycCnt = sample->cyc_cnt;
        row.Weight = sample->weight;
        row.Cpumode = sample->cpumode;
        row.AddrCorrelatesSym = sample->addr_correlates_sym;
        row.Event = eventName;
        row.MachinePid = (uint)sample->machine_pid;
        row.Vcpu = (uint)sample->vcpu;
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
