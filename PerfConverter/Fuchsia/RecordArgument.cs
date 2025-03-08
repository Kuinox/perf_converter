namespace PerfConverter.Fuchsia;

/// <summary>
/// Base class for arguments in an event record
/// </summary>
public abstract class RecordArgument
{
    protected readonly string Name;
    protected readonly ushort NameStringRef;

    protected RecordArgument(string name, ushort nameStringRef)
    {
        Name = name;
        NameStringRef = nameStringRef;
    }

    /// <summary>
    /// Get size of the argument in 8-byte words
    /// </summary>
    public abstract int GetSizeInWords();
    
    /// <summary>
    /// Write the argument to the binary stream
    /// </summary>
    public abstract void Write(BinaryWriter writer);
}