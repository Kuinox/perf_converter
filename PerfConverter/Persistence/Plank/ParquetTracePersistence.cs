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
        PerfDlfilterAl* ip,
        PerfDlfilterAl* address,
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
        row.Ip = ip->addr;
        row.IpLocationId = ipLocationId;
        row.Addr = address == null ? 0UL : address->addr;
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

        row.IpSymoff = ip->symoff;
        row.SetIpSym(EntryContentPool.Shared.RentByteMemoryOwnerFromNullTerminatedPtr((nint)ip->sym));
        row.IpSymStart = ip->sym_start;
        row.IpSymEnd = ip->sym_end;
        row.IpDso = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)ip->dso);
        row.IpSymBinding = ip->sym_binding;
        row.IpIs64Bit = ip->is_64_bit;
        row.IpIsKernelIp = ip->is_kernel_ip;
        row.IpBuildId = EntryContentPool.Shared.GetByteMemory(new ReadOnlySpan<byte>(ip->buildid, ip->buildid_size));
        row.IpFiltered = ip->filtered;
        row.IpComm = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)ip->comm);

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
            row.AddressSymoff = address->symoff;
            row.SetAddressSym(EntryContentPool.Shared.RentByteMemoryOwnerFromNullTerminatedPtr((nint)address->sym));
            row.AddressSymStart = address->sym_start;
            row.AddressSymEnd = address->sym_end;
            row.AddressDso = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)address->dso);
            row.AddressSymBinding = address->sym_binding;
            row.AddressIs64Bit = address->is_64_bit;
            row.AddressIsKernelIp = address->is_kernel_ip;
            row.AddressBuildId = EntryContentPool.Shared.GetByteMemory(new ReadOnlySpan<byte>(address->buildid, address->buildid_size));
            row.AddressFiltered = address->filtered;
            row.AddressComm = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)address->comm);
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
        var writer = TraceSampleRowSchema.CreateRowWriter(fileStream, onFlush);

        return new ParquetTracePersistence(writer);
    }
}
