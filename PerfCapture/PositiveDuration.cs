namespace PerfCapture;

public readonly record struct PositiveDuration
{
    public PositiveDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Duration must be positive.");

        Value = value;
    }

    public TimeSpan Value { get; }

    public override string ToString() => Value.ToString();

    public static implicit operator PositiveDuration(TimeSpan value) => new(value);

    public static implicit operator TimeSpan(PositiveDuration duration) => duration.Value;
}
