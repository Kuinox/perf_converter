using PerfConverter.Entry;
using System.Collections.Generic;
using System.IO;
using Temp.Schema.FuchsiaTraceFormat;

namespace PerfToPerfetto;

public sealed class TraceProcessor(string fileName = "out.ftf", TimestampMode mode = TimestampMode.Time) : IDisposable
{
    readonly Dictionary<ulong, TraceEntry> _traces = [];
    readonly Caches _caches = new();

    FileStream? _file;
    BufferedStream? _out;

    public void Start()
    {
        _file = File.Create(fileName);
        _out = new BufferedStream(_file, 1 << 16);
        Writer.WriteHeader(_out);
    }

    internal void PopFrame(Stack<StackRange> stack, TraceEntry cur)
    {
        if (stack.Count == 0)
            return;

        var range = stack.Pop();
        if (!_traces.TryGetValue(range.StartTrace, out var start))
            return;

        var insnDelta = cur.InsnCnt - start.InsnCnt;
        var cycDelta = cur.CycCnt - start.CycCnt;
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);

        Writer.WriteFrameEnd(_out!, _caches, timestamp, pidTid,
            insnDelta, cycDelta, 0,
            start.Time, cur.Time);
    }

    internal void PopUnknownFrame(Stack<StackRange> stack, TraceEntry cur)
    {
        if (stack.Count == 0)
            return;

        var range = stack.Pop();
        if (!_traces.TryGetValue(range.StartTrace, out var start))
            return;

        var insnDelta = cur.InsnCnt - start.InsnCnt;
        var cycDelta = cur.CycCnt - start.CycCnt;
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.IpSym ?? cur.AddressSym ?? "UNKNOWN";

        Writer.WriteFrameFull(_out!, _caches, timestamp, pidTid,
            insnDelta, cycDelta, 0,
            symbol, timestamp,
            start.Time, cur.Time);
    }

    internal void PushFrame(Stack<StackRange> stack, TraceEntry cur)
    {
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.AddressSym ?? cur.IpSym ?? "TRACE";

        Writer.WriteFrameStart(_out!, _caches,
            ChooseTimestamp(cur),
            pidTid, symbol);

        stack.Push(new StackRange { StartTrace = cur.Id });
        _traces[cur.Id] = cur;
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
        TimestampMode.Instructions => e.InsnCnt,
        _ => e.Time
    };
}
