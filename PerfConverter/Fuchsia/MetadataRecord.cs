using System.Text;

namespace PerfConverter.Fuchsia;

public class MetadataRecord : Record
{
    private readonly MetadataType _metadataType;
    private readonly uint _providerId;  // 32 bits
    private readonly string? _data;      // Optional provider name for ProviderInfo

    public MetadataRecord(MetadataType type, uint providerId = 0, string? data = null)
    {
        _metadataType = type;
        _providerId = providerId;
        _data = data;
    }

    protected override byte GetRecordType() => 0;

    protected override int GetRecordSizeInWords()
    {
        var size = 1; // Header word
        if (_data != null)
            size += (AlignTo8Bytes(Encoding.UTF8.GetByteCount(_data))) / WORD_SIZE;
        return size;
    }

    protected override void WriteRecordData(BinaryWriter writer)
    {
        // Write metadata type and provider ID in header
        var headerData = ((ulong)_metadataType << 16) | ((ulong)_providerId << 20);
        if (_data != null)
        {
            var length = Encoding.UTF8.GetByteCount(_data);
            headerData |= ((ulong)length << 52);
        }
        writer.Write(headerData);

        if (_data != null)
            WriteAlignedString(writer, _data);
    }
}
