namespace PerfCapture;

public readonly record struct ByteSize
{
    public ByteSize(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Byte size must be positive.");

        Value = value;
    }

    public long Value { get; }

    public static ByteSize FromMebibytes(long value) => new(checked(value * 1024 * 1024));

    public override string ToString() => Value.ToString();

    public static implicit operator ByteSize(long value) => new(value);

    public static implicit operator long(ByteSize size) => size.Value;
}
