using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Temp.Schema.FuchsiaTraceFormat;

public static class Writer
{
    public static void WriteInfoArgs(Stream w, ulong insns, ulong cycles, ulong footprint, string timespan) =>
        Impl.WriteInfoArgs(w, insns, cycles, footprint, timespan);

    public static (byte nargs, int size) InfoNArgsSize(string timespan) =>
        Impl.InfoNArgsSize(timespan);

    public static void WriteHeader(Stream w) => Impl.WriteHeader(w);

    public static void WriteFrameStart(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid, string symbol) =>
        Impl.WriteFrameStart(w, c, timestamp, pidTid, symbol);

    public static void WriteFrameEnd(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid,
                                     ulong insns, ulong cycles, ulong footprint,
                                     ulong tsStart, ulong tsEnd) =>
        Impl.WriteFrameEnd(w, c, timestamp, pidTid, insns, cycles, footprint, tsStart, tsEnd);

    public static void WriteFrameFull(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid,
                                      ulong insns, ulong cycles, ulong footprint, string symbol,
                                      ulong endTimestamp, ulong tsStart, ulong tsEnd) =>
        Impl.WriteFrameFull(w, c, timestamp, pidTid, insns, cycles, footprint, symbol, endTimestamp, tsStart, tsEnd);
}
