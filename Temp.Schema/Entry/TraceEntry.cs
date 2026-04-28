using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using Temp.Schema.Entry;

namespace PerfConverter.Entry;

[StructLayout(LayoutKind.Sequential)]
public struct TraceEntry
{
    public ulong Id;
    public ulong PerfId;
    public ushort InstructionLatency;
    public uint Pid;
    public uint Tid;
    public ulong Time;
    public uint Cpu;
    public DLFilterFlag Flags;
    public ulong Period;
    public ulong InsnCnt;
    public ulong CycCnt;
    public ulong Weight;
    public byte Cpumode;
    public byte AddrCorrelatesSym;
    public ReadOnlyMemory<byte> Event;
    public uint MachinePid;
    public uint Vcpu;
    public ReadOnlyMemory<byte>? SourceFileName;
    public uint SourceLineNumber;

    public ulong IpAddress;
    public uint IpSymoff;
    public ReadOnlyMemory<byte>? IpSym;
    public ulong IpSymStart;
    public ulong IpSymEnd;
    public ReadOnlyMemory<byte>? IpDso;
    public byte IpSymBinding;
    public byte IpIs64Bit;
    public byte IpIsKernelIp;
    public ReadOnlyMemory<byte> IpBuildId;
    public byte IpFiltered;
    public ReadOnlyMemory<byte>? IpComm;

    public bool HaveAddress;
    public ulong AddressAddress;
    public uint AddressSymoff;
    public ReadOnlyMemory<byte>? AddressSym;
    public ulong AddressSymStart;
    public ulong AddressSymEnd;
    public ReadOnlyMemory<byte>? AddressDso;
    public byte AddressSymBinding;
    public byte AddressIs64Bit;
    public byte AddressIsKernelIp;
    public ReadOnlyMemory<byte>? AddressBuildId;
    public byte AddressFiltered;
    public ReadOnlyMemory<byte>? AddressComm;

    public static unsafe TraceEntry CreateFromPerf(
        PerfDlFilterSample* sample,
        PerfDlfilterAl* ip,
        PerfDlfilterAl* address,
        ulong id,
        ReadOnlyMemory<byte>? srcFilePath,
        uint lineNumber,
        ReadOnlyMemory<byte> eventName)
    {
        var entry = new TraceEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = (DLFilterFlag)sample->flags,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = eventName,
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu,
            SourceFileName = srcFilePath,
            SourceLineNumber = lineNumber,

            IpAddress = ip->addr,
            IpSymoff = ip->symoff,
            IpSym = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)ip->sym),
            IpSymStart = ip->sym_start,
            IpSymEnd = ip->sym_end,
            IpDso = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)ip->dso),
            IpSymBinding = ip->sym_binding,
            IpIs64Bit = ip->is_64_bit,
            IpIsKernelIp = ip->is_kernel_ip,
            IpBuildId = EntryContentPool.Shared.GetByteMemory(new ReadOnlySpan<byte>(ip->buildid, ip->buildid_size)),
            IpFiltered = ip->filtered,
            IpComm = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)ip->comm)
        };

        if (address != null)
        {
            entry.HaveAddress = true;
            entry.AddressAddress = address->addr;
            entry.AddressSymoff = address->symoff;
            entry.AddressSym = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)address->sym);
            entry.AddressSymStart = address->sym_start;
            entry.AddressSymEnd = address->sym_end;
            entry.AddressDso = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)address->dso);
            entry.AddressSymBinding = address->sym_binding;
            entry.AddressIs64Bit = address->is_64_bit;
            entry.AddressIsKernelIp = address->is_kernel_ip;
            entry.AddressBuildId = EntryContentPool.Shared.GetByteMemory(new ReadOnlySpan<byte>(address->buildid, address->buildid_size));
            entry.AddressFiltered = address->filtered;
            entry.AddressComm = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)address->comm);
        }

        return entry;
    }
}
