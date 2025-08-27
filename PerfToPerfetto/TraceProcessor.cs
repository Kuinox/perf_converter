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

    // Track frame scopes and their accumulated instruction/cycle counts
    readonly Stack<FrameScope> _frameScopes = new();

    // Track the sequence of observed names per (pid, tid) so we can build an accumulated thread name
    // Track last emitted thread name per (pid, tid) so we only emit when it changes.
    readonly Dictionary<(uint pid, uint tid), string> _threadNames = new();

    // Track global accumulated counts from the start of the file for unknown frames
    ulong _globalAccumulatedInsnCnt = 0;
    ulong _globalAccumulatedCycCnt = 0;

    class FrameScope
    {
        public TraceEntry StartTrace;
        public ulong AccumulatedInsnCnt;
        public ulong AccumulatedCycCnt;
    }

    public void Start()
    {
        _file = File.Create(fileName);
        _out = new BufferedStream(_file, 1 << 16);
        Writer.WriteHeader(_out);
    }

    internal void ProcessTrace(TraceEntry trace)
    {
        string? comm = null;
        if (!string.IsNullOrEmpty(trace.AddressComm))
            comm = trace.AddressComm;
        else if (!string.IsNullOrEmpty(trace.IpComm))
            comm = trace.IpComm;

        var haveComm = !string.IsNullOrEmpty(comm);

        if (haveComm)
        {
            var key = (trace.Pid, trace.Tid);
            // Only emit if changed for this thread.
            if (!_threadNames.TryGetValue(key, out var last) || last != comm)
            {
                _threadNames[key] = comm!;
                var pidTid = ((ulong)trace.Pid, (ulong)trace.Tid);
                var timestamp = ChooseTimestamp(trace);
                Writer.WriteThreadName(_out!, _caches, timestamp, pidTid, comm!);
            }
        }



        // Accumulate global instruction and cycle counts
        _globalAccumulatedInsnCnt += trace.InsnCnt;
        _globalAccumulatedCycCnt += trace.CycCnt;

        // Accumulate instruction and cycle counts for all active frame scopes
        foreach (var scope in _frameScopes)
        {
            scope.AccumulatedInsnCnt += trace.InsnCnt;
            scope.AccumulatedCycCnt += trace.CycCnt;
        }
    }

    internal void PopFrame(TraceEntry start, TraceEntry cur)
    {
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);

        // Get accumulated counts from the frame scope
        var scope = _frameScopes.Pop();

        Writer.WriteFrameEnd(_out!, _caches, timestamp, pidTid,
            scope.AccumulatedInsnCnt, scope.AccumulatedCycCnt, 0, // accumulated insns, cycles, footprint
            start.Time, cur.Time);
    }

    internal void PopUnknownFrame(TraceEntry firstTraceOfFile, TraceEntry cur)
    {
        var timestamp = ChooseTimestamp(cur);
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.IpSym ?? cur.AddressSym ?? "UNKNOWN";

        // For unknown frames, use the global accumulated counts from the beginning of the file
        Writer.WriteFrameFull(_out!, _caches, timestamp, pidTid,
            _globalAccumulatedInsnCnt, _globalAccumulatedCycCnt, 0, // Global accumulated counts
            symbol, timestamp,
            firstTraceOfFile.Time, cur.Time); // TODO: use first instruction of trace file.
    }

    internal void PushFrame(TraceEntry cur)
    {
        var pidTid = ((ulong)cur.Pid, (ulong)cur.Tid);
        var symbol = cur.AddressSym ?? cur.IpSym ?? "TRACE";

        // Create a new frame scope to track accumulated counts
        var scope = new FrameScope
        {
            StartTrace = cur,
            AccumulatedInsnCnt = 0,
            AccumulatedCycCnt = 0
        };
        _frameScopes.Push(scope);

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
