namespace PerfCapture;

public abstract record PerfCaptureBufferPolicy
{
    public static PerfCaptureBufferPolicy Growing(PerfBufferSize? dataBuffer = null, PerfBufferSize? auxBuffer = null)
        => new GrowingBufferPolicy(dataBuffer, auxBuffer);

    public static PerfCaptureBufferPolicy Circular(
        PerfBufferSize? dataBuffer = null,
        PerfBufferSize? auxBuffer = null,
        ByteSize? snapshotSize = null)
        => new CircularBufferPolicy(dataBuffer, auxBuffer, snapshotSize);
}
