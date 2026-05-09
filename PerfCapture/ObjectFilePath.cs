namespace PerfCapture;

public sealed record ObjectFilePath
{
    public ObjectFilePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Object file path must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator ObjectFilePath(string value) => new(value);
}
