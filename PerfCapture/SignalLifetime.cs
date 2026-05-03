namespace PerfCapture;

public sealed record SignalLifetime(PerfSignal Signal) :
    PerfCaptureLifetime,
    ICommandCaptureLifetime,
    IExternalCaptureLifetime;
