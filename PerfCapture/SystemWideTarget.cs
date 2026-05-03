namespace PerfCapture;

public sealed record SystemWideTarget(string? CpuList = null) : AttachedPerfCaptureTarget;
