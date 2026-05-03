namespace PerfCapture;

public sealed record ThreadTarget(PerfThreadId ThreadId) : AttachedPerfCaptureTarget;
