namespace PerfCapture;

public sealed record TraceAddressRange(
    TraceAddressExpression Start,
    ByteSize? Size = null,
    ObjectFilePath? ObjectFile = null);
