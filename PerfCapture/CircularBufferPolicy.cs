namespace PerfCapture;

public sealed record CircularBufferPolicy(
    PerfBufferSize? DataBuffer = null,
    PerfBufferSize? AuxBuffer = null,
    ByteSize? SnapshotSize = null) : PerfCaptureBufferPolicy;
