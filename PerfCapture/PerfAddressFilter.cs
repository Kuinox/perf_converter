namespace PerfCapture;

public sealed record PerfAddressFilter(
    PerfAddressFilterKind Kind,
    TraceAddressRange Range)
{
    public static PerfAddressFilter Filter(TraceAddressExpression start, ByteSize? size = null, ObjectFilePath? objectFile = null)
        => new(PerfAddressFilterKind.Filter, new TraceAddressRange(start, size, objectFile));

    public static PerfAddressFilter Start(TraceAddressExpression start, ByteSize? size = null, ObjectFilePath? objectFile = null)
        => new(PerfAddressFilterKind.Start, new TraceAddressRange(start, size, objectFile));

    public static PerfAddressFilter Stop(TraceAddressExpression start, ByteSize? size = null, ObjectFilePath? objectFile = null)
        => new(PerfAddressFilterKind.Stop, new TraceAddressRange(start, size, objectFile));

    public static PerfAddressFilter TraceStop(TraceAddressExpression start, ByteSize? size = null, ObjectFilePath? objectFile = null)
        => new(PerfAddressFilterKind.TraceStop, new TraceAddressRange(start, size, objectFile));
}
