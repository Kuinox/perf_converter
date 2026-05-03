namespace PerfCapture;

public sealed record CommandCaptureSpec : PerfCaptureSpec
{
    public required CommandTarget Target { get; init; }

    public ICommandCaptureLifetime Lifetime { get; init; } = new TargetExitLifetime();
}
