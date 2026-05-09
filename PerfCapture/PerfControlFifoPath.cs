namespace PerfCapture;

public sealed record PerfControlFifoPath
{
    public PerfControlFifoPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Perf control FIFO path must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator PerfControlFifoPath(string value) => new(value);
}
