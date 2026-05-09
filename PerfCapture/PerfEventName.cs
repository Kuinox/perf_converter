namespace PerfCapture;

public sealed record PerfEventName
{
    public PerfEventName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Perf event name must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator PerfEventName(string value) => new(value);
}
