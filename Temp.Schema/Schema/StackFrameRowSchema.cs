using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class StackFrameRowSchema
{
    [ParquetColumn("frameId", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong FrameId { get; set; }

    [ParquetColumn("tid", Encodings = [EncodingKind.RleDictionary])]
    public uint Tid { get; set; }

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

    [ParquetColumn("startCpu", Encodings = [EncodingKind.RleDictionary])]
    public uint StartCpu { get; set; }

    [ParquetColumn("endCpu", Encodings = [EncodingKind.RleDictionary])]
    public uint EndCpu { get; set; }

    [ParquetColumn("startReason", Encodings = [EncodingKind.RleDictionary])]
    public byte StartReason { get; set; }

    [ParquetColumn("endReason", Encodings = [EncodingKind.RleDictionary])]
    public byte EndReason { get; set; }
}
