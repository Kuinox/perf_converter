using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class SourceLocationRowSchema
{
    [ParquetColumn("id", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Id { get; set; }

    [ParquetColumn("buildId", Encodings = [EncodingKind.Plain])]
    public ReadOnlyMemory<byte> BuildId { get; set; }

    [ParquetColumn("dso", Encodings = [EncodingKind.RleDictionary])]
    public ReadOnlyMemory<byte> Dso { get; set; }

    [ParquetColumn("relativeAddress", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong RelativeAddress { get; set; }

    [ParquetColumn("symbol", Encodings = [EncodingKind.RleDictionary])]
    public ReadOnlyMemory<byte>? Symbol { get; set; }

    [ParquetColumn("symbolOffset", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public uint SymbolOffset { get; set; }

    [ParquetColumn("symbolStart", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong SymbolStart { get; set; }

    [ParquetColumn("symbolEnd", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong SymbolEnd { get; set; }

    [ParquetColumn("sourceFileName", Encodings = [EncodingKind.RleDictionary])]
    public ReadOnlyMemory<byte>? SourceFileName { get; set; }

    [ParquetColumn("sourceLineNumber", Encodings = [EncodingKind.RleDictionary])]
    public uint SourceLineNumber { get; set; }

    [ParquetColumn("sourceColumnNumber", Encodings = [EncodingKind.RleDictionary])]
    public uint SourceColumnNumber { get; set; }

    [ParquetColumn("inlineDepth", Encodings = [EncodingKind.RleDictionary])]
    public uint InlineDepth { get; set; }

    [ParquetColumn("keyStrength", Encodings = [EncodingKind.RleDictionary])]
    public byte KeyStrength { get; set; }

    [ParquetColumn("isKernelIp", Encodings = [EncodingKind.RleDictionary])]
    public byte IsKernelIp { get; set; }
}
