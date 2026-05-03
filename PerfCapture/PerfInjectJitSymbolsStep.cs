namespace PerfCapture;

public sealed record PerfInjectJitSymbolsStep(
    PerfOutputPath OutputPath,
    bool Force = true) : PerfPostProcessingStep;
