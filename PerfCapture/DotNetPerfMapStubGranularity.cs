namespace PerfCapture;

public readonly record struct DotNetPerfMapStubGranularity
{
    public DotNetPerfMapStubGranularity(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Perf-map stub granularity must not be negative.");

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();

    public static implicit operator DotNetPerfMapStubGranularity(int value) => new(value);

    public static implicit operator int(DotNetPerfMapStubGranularity granularity) => granularity.Value;
}
