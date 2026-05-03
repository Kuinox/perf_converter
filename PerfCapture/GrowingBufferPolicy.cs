namespace PerfCapture;

public sealed record GrowingBufferPolicy(
    PerfBufferSize? DataBuffer = null,
    PerfBufferSize? AuxBuffer = null) : PerfCaptureBufferPolicy;
