namespace PerfCapture;

public sealed record PerfOutputPath
{
    public PerfOutputPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Output path must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator PerfOutputPath(string value) => new(value);
}
