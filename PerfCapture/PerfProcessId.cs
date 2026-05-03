namespace PerfCapture;

public readonly record struct PerfProcessId
{
    public PerfProcessId(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Process ID must be positive.");

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();

    public static implicit operator PerfProcessId(int value) => new(value);

    public static implicit operator int(PerfProcessId processId) => processId.Value;
}
