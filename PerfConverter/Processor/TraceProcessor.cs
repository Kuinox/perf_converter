using System.Runtime.InteropServices;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using Temp.Core;

namespace PerfConverter.Processor;

public unsafe class TraceProcessor(IStringProcessor eventProcessor, Func<string, IPersister<TraceSampleEntry>> persistanceFactory) : ITraceProcessor
{
    ulong _totalSamples = 0;
    readonly Dictionary<string, IPersister<TraceSampleEntry>> _persistences = [];
    readonly Dictionary<(int, int), string> _keys = [];
    public ulong FilterEventEarly(PerfDlFilterSample* sample)
    {
        var id = _totalSamples++;
        ref var key = ref CollectionsMarshal.GetValueRefOrAddDefault(_keys, (sample->pid, sample->tid), out _);
        key ??= $"{sample->pid}/{sample->tid}";
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_persistences, key, out _);
        persistence ??= persistanceFactory(key);
        
        var eventString = Marshal.PtrToStringUTF8(sample->@event);
        var eventId = eventString != null ? eventProcessor.Process(eventString) : 0;
        
        persistence.Persist(new TraceSampleEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = (uint)sample->pid,
            Tid = (uint)sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = (DLFilterFlag)sample->flags,
            Ip = (ulong)sample->ip,
            Addr = (ulong)sample->addr,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            EventId = eventId,
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu
        });

        return id;
    }
}
