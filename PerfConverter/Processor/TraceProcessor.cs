using System.Runtime.InteropServices;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using Temp.Core;

namespace PerfConverter.Processor;

public unsafe class TraceProcessor(IPersister<TraceSampleEntry> persistence) : ITraceProcessor
{
    ulong _totalSamples = 0;

    public unsafe ulong FilterEventEarly(PerfDlFilterSample* sample)
    {
        var id = _totalSamples++;
        persistence.Persist(new TraceSampleEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = (uint)sample->pid,
            Tid = (uint)sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = sample->flags,
            Ip = (ulong)sample->ip,
            Addr = (ulong)sample->addr,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = Marshal.PtrToStringUTF8(sample->@event),
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu
        });

        return id;
    }
}
    