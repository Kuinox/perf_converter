namespace PerfCapture;

public sealed record DotNetPerfMapOptions
{
    public bool PerfMapEnabled { get; init; } = true;

    public DotNetPerfMapStubGranularity StubGranularity { get; init; } = new(2);

    public bool EnableWriteXorExecute { get; init; }

    public IReadOnlyDictionary<string, string?> ToEnvironment()
    {
        return new Dictionary<string, string?>
        {
            ["DOTNET_PerfMapEnabled"] = PerfMapEnabled ? "1" : "0",
            ["DOTNET_PerfMapStubGranularity"] = StubGranularity.Value.ToString(),
            ["DOTNET_EnableWriteXorExecute"] = EnableWriteXorExecute ? "1" : "0"
        };
    }
}
