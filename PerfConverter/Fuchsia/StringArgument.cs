using System.Text;

namespace PerfConverter.Fuchsia;

/// <summary>
/// Represents a string argument for an event record
/// </summary>
public class StringArgument : RecordArgument
{
    private readonly string _value;
    
    public StringArgument(string name, string value) 
        : base(name, 0)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("String value cannot be null or empty");
        _value = value;
    }
    
    public StringArgument(ushort nameRef, string value) 
        : base(string.Empty, nameRef)
    {
        if(string.IsNullOrEmpty(value))
            throw new ArgumentException("String value cannot be null or empty");
        _value = value;
    }
    
    public override int GetSizeInWords()
    {
        var stringBytes = Encoding.UTF8.GetByteCount(_value);
        var alignedSize = Record.AlignTo8Bytes(stringBytes) / Record.WORD_SIZE;
        return 1 + alignedSize; // Type/size/name/flags + string content
    }
    
    public override void Write(BinaryWriter writer)
    {
        var stringBytes = Encoding.UTF8.GetByteCount(_value);
        var size = 1 + (Record.AlignTo8Bytes(stringBytes) / Record.WORD_SIZE);
        
        // Argument header (type 6 for string): 
        // [0..3]   - Type (4 bits)
        // [4..15]  - Size in words (12 bits)
        // [16..31] - Name reference (16 bits)
        // [32..46] - String length (15 bits)
        // [47]     - Is inline (1 bit)
        // [48..63] - Reserved (16 bits)
        var header = 6UL | ((ulong)size << 4) | ((ulong)NameStringRef << 16) | ((ulong)stringBytes << 32) | (1UL << 47);
        writer.Write(header);
        
        // Write string bytes
        var bytes = Encoding.UTF8.GetBytes(_value);
        writer.Write(bytes);
        
        // Pad to 8-byte boundary
        var padding = Record.AlignTo8Bytes(bytes.Length) - bytes.Length;
        if (padding > 0)
        {
            writer.Write(new byte[padding]);
        }
    }
}