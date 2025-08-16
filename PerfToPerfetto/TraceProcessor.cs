using PerfConverter.Entry;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Temp.Schema.FuchsiaTraceFormat;

namespace PerfToPerfetto;

public sealed class TraceProcessor(string fileName = "out.ftf", TimestampMode mode = TimestampMode.Time) : IDisposable
{
    readonly Caches _caches = new();

    FileStream? _file;
    BufferedStream? _out;

    public void Start()
    {
        _file = File.Create(fileName);
        _out = new BufferedStream(_file, 1 << 16);
        Writer.WriteHeader(_out);
    }

    internal void PopFrame(TraceEntry start, TraceEntry cur)
    {
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);

        Writer.WriteFrameEnd(_out!, _caches, timestamp, pidTid,
            0, 0, 0, //TODO: insns, cycles
            start.Time, cur.Time);
    }

    internal void PopUnknownFrame(TraceEntry firstTraceOfFile, TraceEntry cur)
    {
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.IpSym ?? cur.AddressSym ?? "UNKNOWN";

        Writer.WriteFrameFull(_out!, _caches, timestamp, pidTid,
            cur.InsnCnt, 0, 0,
            symbol, timestamp,
            firstTraceOfFile.Time, cur.Time); // TODO: use first instruction of trace file.
    }

    internal void PushFrame(TraceEntry cur)
    {
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.AddressSym ?? cur.IpSym ?? "TRACE";

        Writer.WriteFrameStart(_out!, _caches,
            ChooseTimestamp(cur),
            pidTid, symbol);
    }

    public void Dispose()
    {
        _out?.Flush();
        _out?.Dispose();
        _file?.Dispose();
    }

    ulong ChooseTimestamp(TraceEntry e) => mode switch
    {
        TimestampMode.Cycles => e.CycCnt,
        _ => e.Time
    };
}
