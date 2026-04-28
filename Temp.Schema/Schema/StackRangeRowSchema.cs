using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class StackRangeRowSchema
{
    [ParquetColumn("startTrace", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong StartTrace { get; set; }

    [ParquetColumn("endTrace", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong EndTrace { get; set; }
}
