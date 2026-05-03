namespace PerfCapture;

public sealed record CommandTarget(
    CommandExecutable FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null) : PerfCaptureTarget;
