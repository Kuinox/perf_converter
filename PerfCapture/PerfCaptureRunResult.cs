namespace PerfCapture;

public sealed record PerfCaptureRunResult
{
    public required PerfCapturePlan Plan { get; init; }

    public required PerfCommandResult RecordResult { get; init; }

    public IReadOnlyList<PerfCommandResult> PostProcessingResults { get; init; } = [];

    public bool Succeeded => RecordResult.Succeeded && PostProcessingResults.All(static result => result.Succeeded);
}
