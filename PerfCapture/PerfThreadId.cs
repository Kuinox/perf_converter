namespace PerfCapture;

public readonly record struct PerfThreadId
{
    public PerfThreadId(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Thread ID must be positive.");

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();

    public static implicit operator PerfThreadId(int value) => new(value);

    public static implicit operator int(PerfThreadId threadId) => threadId.Value;
}
