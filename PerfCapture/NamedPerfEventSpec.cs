namespace PerfCapture;

public sealed record NamedPerfEventSpec(
    PerfEventName Name,
    IReadOnlyList<string>? Terms = null) : PerfEventSpec;
