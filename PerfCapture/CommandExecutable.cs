namespace PerfCapture;

public sealed record CommandExecutable
{
    public CommandExecutable(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Command executable must not be empty.", nameof(value));

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator CommandExecutable(string value) => new(value);
}
