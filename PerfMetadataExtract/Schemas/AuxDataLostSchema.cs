using Parquet.Data;
using Parquet.Schema;

namespace PerfMetadataExtract.Schemas;

/// <summary>
/// Provides Parquet schema for AuxDataLostEntry.
/// </summary>
public static class AuxDataLostSchema
{
    public static DataField<ulong> Time { get; } = new DataField<ulong>("time");
    public static DataField<ulong> Pid { get; } = new DataField<ulong>("pid");
    public static DataField<ulong> Tid { get; } = new DataField<ulong>("tid");
    public static DataField<ulong> Cpu { get; } = new DataField<ulong>("cpu");
    public static DataField<ulong> Flags { get; } = new DataField<ulong>("flags");

    /// <summary>
    /// Gets the complete schema for AuxDataLostEntry.
    /// </summary>
    public static ParquetSchema Schema { get; } = new ParquetSchema(Time, Pid, Tid, Cpu, Flags);
}