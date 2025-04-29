using System.Data.Common;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Xml.Schema;
using Dapper;
using Microsoft.Data.Sqlite;
using PerfConverter.Persistance;

namespace PerfConverter;

public unsafe class TraceProcessor : ITraceProcessor
{
    private ulong _totalSamples = 0;
    private Channel<TraceSample> _channel;
    private Task _workThread;
    private TraceProcessor(ITracePersistance persistance)
    {
        var batchSize = 1_000_000;
        _channel = Channel.CreateBounded<TraceSample>(batchSize);
        _workThread = BackgroundBatching<TraceSample>.Run(batchSize, _channel.Reader, persistance.Persist);
    }

    public unsafe long FilterEventEarly(PerfDlFilterSample* sample)
    {
        _channel.Writer.Write(new TraceSample
        {
            Id = _totalSamples++,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = sample->cpu,
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

    public void Close()
    {
        _channel.Writer.Complete();
        _workThread.Wait();
    }
}
