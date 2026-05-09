namespace PerfCapture;

public sealed record TraceAddressExpression
{
    public TraceAddressExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Trace address expression must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator TraceAddressExpression(string value) => new(value);
}
