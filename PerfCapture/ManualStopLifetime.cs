namespace PerfCapture;

public sealed record ManualStopLifetime :
    PerfCaptureLifetime,
    ICommandCaptureLifetime,
    IExternalCaptureLifetime;
