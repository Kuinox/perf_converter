namespace PerfCapture;

public abstract record PerfCaptureSpec
{
    public required PerfOutputPath OutputPath { get; init; }

    public PerfPrivilegeMode Privilege { get; init; } = PerfPrivilegeMode.CurrentUser;

    public IntelPtEventSpec Event { get; init; } = IntelPtEventSpec.UserOnly();

    public PerfCaptureBufferPolicy Buffer { get; init; } = PerfCaptureBufferPolicy.Growing();

    public bool UseKernelCore { get; init; }

    public bool OverwriteOutput { get; init; }

    public IReadOnlyList<string> ExtraPerfRecordArguments { get; init; } = [];

    public IReadOnlyList<PerfPostProcessingStep> PostProcessingSteps { get; init; } = [];
}
