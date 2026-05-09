namespace PerfCapture;

public sealed record PerfCapturePlan
{
    public required PerfCommandPlan RecordCommand { get; init; }

    public IReadOnlyList<PerfCommandPlan> PostProcessingCommands { get; init; } = [];

    public IReadOnlyList<PerfCaptureRequirement> Requirements { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
