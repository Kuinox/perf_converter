namespace PerfCapture;

public sealed record PerfSignal
{
    public PerfSignal(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Signal name must not be empty.", nameof(name));

        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;

    public static implicit operator PerfSignal(string name) => new(name);
}
