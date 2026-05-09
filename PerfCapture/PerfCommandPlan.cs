namespace PerfCapture;

public sealed record PerfCommandPlan
{
    public required CommandExecutable FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>();

    public string? WorkingDirectory { get; init; }
}
