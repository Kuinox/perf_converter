using System.Buffers.Binary;

namespace Temp.Schema.FuchsiaTraceFormat;

static class Impl
{
    public static void WriteU64(Stream w, ulong x)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, x);
        w.Write(buf);
    }

    public static void WriteString(Stream w, ReadOnlySpan<byte> s)
    {
        w.Write(s);
        var pad = s.Length % 8;
        if (pad != 0)
        {
            Span<byte> zero = stackalloc byte[8];
            w.Write(zero[..(8 - pad)]);
        }
    }

    static ulong DivUp(ulong a, ulong b) => a / b + (a % b != 0 ? 1UL : 0UL);
    public static ulong WordsForBytes(ulong x) => DivUp(x, 8);

    static string PrintTimestamp(ulong nanos)
    {
        var secs = nanos / 1_000_000_000UL;
        var rem = nanos % 1_000_000_000UL;
        return $"{secs}.{rem:000000000}";
    }

    static string PrintTimespan((ulong start, ulong end) ts) =>
        $"{PrintTimestamp(ts.start)},{PrintTimestamp(ts.end)}";

    static void WriteEventHeader(Caches c, Stream w, EventHeader e)
    {
        const ulong rtype = 4;
        var rsize = 2UL + (ulong)e.ExtraDataSize;

        var nameRef = c.StringCache.GetRef(w, e.Name).Idx;
        var catRef = c.StringCache.GetRef(w, e.Category).Idx;
        var thrRef = c.ThreadCache.GetRef(w, e.PidTid).Idx;

        ulong word =
            rtype |
            rsize << 4 |
            (ulong)e.EType << 16 |
            (ulong)e.NArgs << 20 |
            thrRef << 24 |
            catRef << 32 |
            nameRef << 48;

        WriteU64(w, word);
        WriteU64(w, e.Timestamp);
    }

    public static void WriteInfoArgs(Stream w, ulong insns, ulong cycles, ulong footprint, string timespan)
    {
        WriteU64(w, 4UL | 2UL << 4 | (ulong)InternalString.Instructions << 16);
        WriteU64(w, insns);

        WriteU64(w, 4UL | 2UL << 4 | (ulong)InternalString.Cycles << 16);
        WriteU64(w, cycles);

        WriteU64(w, 4UL | 2UL << 4 | (ulong)InternalString.Footprint << 16);
        WriteU64(w, footprint);

        var tsBytes = (ulong)System.Text.Encoding.UTF8.GetByteCount(timespan);
        var tsSize = 1UL + WordsForBytes(tsBytes);
        WriteU64(
            w,
            6UL
            | tsSize << 4
            | (ulong)InternalString.Timespan << 16
            | tsBytes << 32
            | 1UL << 47
        );
        WriteString(w, System.Text.Encoding.UTF8.GetBytes(timespan));
    }

    public static (byte nargs, int size) InfoNArgsSize(string timespan)
    {
        var len = System.Text.Encoding.UTF8.GetByteCount(timespan);
        return (4, 7 + (int)WordsForBytes((ulong)len));
    }

    public static void WriteHeader(Stream w)
    {
        WriteU64(w, 0x0016547846040010UL);

        const ulong rtype = 0;
        const ulong mtype = 1;
        const string name = "scylla";
        var nameLen = (ulong)System.Text.Encoding.UTF8.GetByteCount(name);
        var rsize = 1UL + WordsForBytes(nameLen);
        const ulong providerId = 0;

        WriteU64(w, rtype | rsize << 4 | mtype << 16 | providerId << 20 | nameLen << 52);
        WriteString(w, System.Text.Encoding.UTF8.GetBytes(name));

        {
            var rsize2 = 1UL;
            var mtype2 = 2UL;
            WriteU64(w, rtype | rsize2 << 4 | mtype2 << 16 | providerId << 20);
        }

        foreach (var e in InternalStrings.AllExceptEmpty())
        {
            var idx = (ulong)e;
            var s = InternalStrings.Get(e);
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var sLen = bytes.Length;
            var rsizeStr = 1UL + WordsForBytes((ulong)sLen);
            const ulong rtypeStr = 2UL;
            WriteU64(w, rtypeStr | rsizeStr << 4 | idx << 16 | (ulong)sLen << 32);
            WriteString(w, bytes);
        }
    }

    public static void WriteFrameStart(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid, string symbol)
    {
        WriteEventHeader(c, w, new EventHeader(
            symbol, "Misc", pidTid, timestamp, 0, 2, 0));
    }

    public static void WriteFrameEnd(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid,
                                     ulong insns, ulong cycles, ulong footprint,
                                     ulong tsStart, ulong tsEnd)
    {
        var ts = PrintTimespan((tsStart, tsEnd));
        var (nargs, size) = InfoNArgsSize(ts);

        WriteEventHeader(c, w, new EventHeader(
            "", "Misc", pidTid, timestamp, nargs, 3, size));

        WriteInfoArgs(w, insns, cycles, footprint, ts);
    }

    public static void WriteFrameFull(Stream w, Caches c, ulong timestamp, (ulong pid, ulong tid) pidTid,
                                      ulong insns, ulong cycles, ulong footprint, string symbol,
                                      ulong endTimestamp, ulong tsStart, ulong tsEnd)
    {
        var ts = PrintTimespan((tsStart, tsEnd));
        var (nargs, size) = InfoNArgsSize(ts);

        WriteEventHeader(c, w, new EventHeader(
            symbol, "Misc", pidTid, timestamp, nargs, 4, 1 + size));

        WriteInfoArgs(w, insns, cycles, footprint, ts);
        WriteU64(w, endTimestamp);
    }
}
