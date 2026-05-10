using PerfConverter.PerfStructs;
using Temp.Schema.Entry;

namespace PerfConverter;

public readonly struct OwnedPerfSample
{
    public uint Pid { get; init; }
    public uint Tid { get; init; }
    public ulong Time { get; init; }
    public ulong Id { get; init; }
    public ulong Period { get; init; }
    public ulong Weight { get; init; }
    public ulong InsnCount { get; init; }
    public ulong CycleCount { get; init; }
    public int Cpu { get; init; }
    public uint Flags { get; init; }
    public byte CpuMode { get; init; }
    public byte AddressCorrelatesSymbol { get; init; }
    public int MachinePid { get; init; }
    public int Vcpu { get; init; }
    public ReadOnlyMemory<byte> EventName { get; init; }

    public static unsafe OwnedPerfSample From(PerfDlFilterSample* sample)
        => new()
        {
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Id = sample->id,
            Period = sample->period,
            Weight = sample->weight,
            InsnCount = sample->insn_cnt,
            CycleCount = sample->cyc_cnt,
            Cpu = sample->cpu,
            Flags = sample->flags,
            CpuMode = sample->cpumode,
            AddressCorrelatesSymbol = sample->addr_correlates_sym,
            MachinePid = sample->machine_pid,
            Vcpu = sample->vcpu,
            EventName = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr(sample->@event)
        };
}
