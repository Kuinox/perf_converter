namespace PerfConverter.Fuchsia;

/// <summary>
/// Represents a numeric (integer) argument for an event record
/// </summary>
public class NumericArgument : RecordArgument
{
    private readonly ulong _value;
    
    public NumericArgument(string name, ulong value) 
        : base(name, 0)
    {
        _value = value;
    }
    
    public NumericArgument(ushort nameRef, ulong value) 
        : base(string.Empty, nameRef)
    {
        _value = value;
    }
    
    public override int GetSizeInWords()
    {
        return 2; // Type/size/name + value = 2 words
    }
    
    public override void Write(BinaryWriter writer)
    {
        // Argument header (type 4): 
        // [0..3]   - Type (4 bits)
        // [4..15]  - Size in words (12 bits)
        // [16..63] - Name reference (48 bits)
        var header = 4UL | (2UL << 4) | ((ulong)NameStringRef << 16);
        writer.Write(header);
        
        // Value
        writer.Write(_value);
    }
}