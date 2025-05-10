using System.Runtime.InteropServices;
using PerfConverter.Entry;
using PerfConverter.Persistence;

namespace PerfConverter.Processor;

public unsafe class TraceProcessor(IPersiter<TraceSampleEntry> persistence) : ITraceProcessor
{
    ulong _totalSamples = 0;

    public unsafe long FilterEventEarly(PerfDlFilterSample* sample)
    {
        persistence.Persit(new TraceSampleEntry
        {
            Id = _totalSamples++,
            PerfId = sample->id,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = sample->cpu,
            Flags = sample->flags,
            Ip = (long)sample->ip,
            Addr = (long)sample->addr,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = Marshal.PtrToStringUTF8(sample->@event),
            MachinePid = sample->machine_pid,
            Vcpu = sample->vcpu
        });

        return (long)sample->id;
    }
}
