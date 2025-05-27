using Parquet.Data;
using Parquet.Schema;
using Temp.Schema;

namespace Temp.Schema;

public static class TraceWithStackSchema
{
    public static DataField<int> SegmentId { get; } = new DataField<int>("segmentId");
    public static DataField<ulong[]> Stack { get; } = new DataField<ulong[]>("stack");

    public static ParquetSchema Schema { get; } = new ParquetSchema(
        TraceSampleSchema.Id,
        TraceSampleSchema.PerfId,
        TraceSampleSchema.Pid,
        TraceSampleSchema.Tid,
        TraceSampleSchema.Time,
        TraceSampleSchema.Cpu,
        TraceSampleSchema.Flags,
        TraceSampleSchema.Ip,
        TraceSampleSchema.Addr,
        TraceSampleSchema.Period,
        TraceSampleSchema.InsnCnt,
        TraceSampleSchema.CycCnt,
        TraceSampleSchema.Weight,
        TraceSampleSchema.Cpumode,
        TraceSampleSchema.AddrCorrelatesSym,
        TraceSampleSchema.EventId,
        TraceSampleSchema.MachinePid,
        TraceSampleSchema.Vcpu,
        SegmentId,
        Stack
    );
}
