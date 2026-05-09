namespace PerfCapture;

public readonly record struct PerfFileDescriptor
{
    public PerfFileDescriptor(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "File descriptor must be non-negative.");

        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();

    public static implicit operator PerfFileDescriptor(int value) => new(value);

    public static implicit operator int(PerfFileDescriptor fileDescriptor) => fileDescriptor.Value;
}
