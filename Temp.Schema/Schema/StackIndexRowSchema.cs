using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class StackIndexRowSchema
{
    [ParquetColumn("pid", Encodings = [EncodingKind.RleDictionary])]
    public uint Pid { get; set; }

    [ParquetColumn("tid", Encodings = [EncodingKind.RleDictionary])]
    public uint Tid { get; set; }

    [ParquetColumn("cpu", Encodings = [EncodingKind.RleDictionary])]
    public uint Cpu { get; set; }

    [ParquetColumn("depth", Encodings = [EncodingKind.RleDictionary])]
    public uint Depth { get; set; }

    [ParquetColumn("startTime", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong StartTime { get; set; }

    [ParquetColumn("endTime", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong EndTime { get; set; }

    [ParquetColumn("startTrace", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong StartTrace { get; set; }

    [ParquetColumn("endTrace", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong EndTrace { get; set; }

    [ParquetColumn("locationId", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong LocationId { get; set; }
}
