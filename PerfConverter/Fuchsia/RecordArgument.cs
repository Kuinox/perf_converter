namespace PerfConverter.Fuchsia;

public abstract class RecordArgument
{
    protected readonly string Name;
    protected readonly ushort NameStringRef;

    protected RecordArgument(string name, ushort nameStringRef)
    {
        Name = name;
        NameStringRef = nameStringRef;
    }

    public abstract int GetSizeInWords();
    public abstract void Write(BinaryWriter writer);
}
