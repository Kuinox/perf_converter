namespace PerfCapture;

public sealed record DurationLifetime(PositiveDuration Duration) :
    PerfCaptureLifetime,
    ICommandCaptureLifetime,
    IExternalCaptureLifetime;
