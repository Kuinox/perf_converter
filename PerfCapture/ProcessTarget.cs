namespace PerfCapture;

public sealed record ProcessTarget(PerfProcessId ProcessId) : AttachedPerfCaptureTarget;
