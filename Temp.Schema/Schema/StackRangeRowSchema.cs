using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class StackRangeRowSchema
{
    [ParquetColumn("startTrace")]
    public ulong StartTrace { get; set; }

    [ParquetColumn("endTrace")]
    public ulong EndTrace { get; set; }
}
