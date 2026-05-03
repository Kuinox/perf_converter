namespace PerfCapture;

public sealed record AttachCaptureSpec : PerfCaptureSpec
{
    public required AttachedPerfCaptureTarget Target { get; init; }

    public IExternalCaptureLifetime Lifetime { get; init; } = new ManualStopLifetime();
}
