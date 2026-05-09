namespace PerfCapture;

public sealed record PerfCallGraphMode
{
    public PerfCallGraphMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Perf call graph mode must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static PerfCallGraphMode FramePointer() => new("fp");

    public static implicit operator PerfCallGraphMode(string value) => new(value);
}
