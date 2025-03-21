using System.Text;

namespace PerfConverter.Fuchsia;

public class StringRecord : Record
{
    private readonly ushort _index;     // 15 bits used
    private readonly string _value;

    public StringRecord(ushort index, string value)
    {
        if (index > 0x7FFF)
            throw new ArgumentException("String index must be <= 0x7FFF");
        _index = index;
        _value = value;
    }

    protected override byte GetRecordType() => 2;

    protected override int GetRecordSizeInWords()
    {
        return 1 + (AlignTo8Bytes(Encoding.UTF8.GetByteCount(_value))) / WORD_SIZE;
    }

    protected override void WriteRecordData(BinaryWriter writer)
    {
        var length = Encoding.UTF8.GetByteCount(_value);
        var headerData = ((ulong)_index << 16) | ((ulong)length << 32);
        writer.Write(headerData);
        WriteAlignedString(writer, _value);
    }
}
