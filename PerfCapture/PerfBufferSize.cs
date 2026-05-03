namespace PerfCapture;

public readonly record struct PerfBufferSize
{
    PerfBufferSize(long value, PerfBufferSizeUnit unit)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Buffer size must be positive.");

        Value = value;
        Unit = unit;
    }

    public long Value { get; }

    public PerfBufferSizeUnit Unit { get; }

    public static PerfBufferSize Pages(int value) => new(value, PerfBufferSizeUnit.Pages);

    public static PerfBufferSize Bytes(ByteSize value) => new(value.Value, PerfBufferSizeUnit.Bytes);

    public static PerfBufferSize Mebibytes(long value) => Bytes(ByteSize.FromMebibytes(value));
}
