namespace PerfCapture;

public abstract record PerfCaptureSpec
{
    public required PerfOutputPath OutputPath { get; init; }

    public PerfEventSpec Event { get; init; } = PerfEventSpec.CpuClock();

    public IReadOnlyList<PerfEventSpec> AdditionalEvents { get; init; } = [];

    public uint? SampleFrequency { get; init; }

    public PerfCallGraphMode? CallGraph { get; init; }

    public PerfCaptureBufferPolicy Buffer { get; init; } = PerfCaptureBufferPolicy.Growing();

    public bool UseKernelCore { get; init; }

    public bool StartDisabled { get; init; }

    public PerfControlChannel? Control { get; init; }

    public IReadOnlyList<PerfAddressFilter> AddressFilters { get; init; } = [];

    public bool OverwriteOutput { get; init; }

    public IReadOnlyList<string> ExtraPerfRecordArguments { get; init; } = [];

    public IReadOnlyList<PerfPostProcessingStep> PostProcessingSteps { get; init; } = [];
}
