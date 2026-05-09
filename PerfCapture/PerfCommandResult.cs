namespace PerfCapture;

public sealed record PerfCommandResult
{
    public required PerfCommandPlan Command { get; init; }

    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public bool Succeeded => ExitCode == 0;
}
