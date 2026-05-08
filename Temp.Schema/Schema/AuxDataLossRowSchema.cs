using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class AuxDataLossRowSchema
{
    [ParquetColumn("id", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Id { get; set; }

    [ParquetColumn("time", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Time { get; set; }

    [ParquetColumn("pid", Encodings = [EncodingKind.RleDictionary])]
    public uint Pid { get; set; }

    [ParquetColumn("tid", Encodings = [EncodingKind.RleDictionary])]
    public uint Tid { get; set; }

    [ParquetColumn("cpu", Encodings = [EncodingKind.RleDictionary])]
    public uint Cpu { get; set; }

    [ParquetColumn("flags", Encodings = [EncodingKind.RleDictionary])]
    public ulong Flags { get; set; }
}
