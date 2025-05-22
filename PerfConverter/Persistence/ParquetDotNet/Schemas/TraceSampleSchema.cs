using Parquet.Data;
using Parquet.Schema;

namespace PerfConverter.Persistence.ParquetDotNet.Schemas;

/// <summary>
/// Provides Parquet schema for TraceSampleEntry.
/// </summary>
public static class TraceSampleSchema
{
    public static DataField<ulong> Id { get; } = new DataField<ulong>("id");
    public static DataField<ulong> PerfId { get; } = new DataField<ulong>("perfId");
    public static DataField<uint> Pid { get; } = new DataField<uint>("pid");
    public static DataField<uint> Tid { get; } = new DataField<uint>("tid");
    public static DataField<ulong> Time { get; } = new DataField<ulong>("time");
    public static DataField<uint> Cpu { get; } = new DataField<uint>("cpu");
    public static DataField<uint> Flags { get; } = new DataField<uint>("flags");
    public static DataField<ulong> Ip { get; } = new DataField<ulong>("ip");
    public static DataField<ulong> Addr { get; } = new DataField<ulong>("addr");
    public static DataField<ulong> Period { get; } = new DataField<ulong>("period");
    public static DataField<ulong> InsnCnt { get; } = new DataField<ulong>("insnCnt");
    public static DataField<ulong> CycCnt { get; } = new DataField<ulong>("cycCnt");
    public static DataField<ulong> Weight { get; } = new DataField<ulong>("weight");
    public static DataField<byte> Cpumode { get; } = new DataField<byte>("cpumode");
    public static DataField<byte> AddrCorrelatesSym { get; } = new DataField<byte>("addrCorrelatesSym");
    public static DataField<ulong> EventId { get; } = new DataField<ulong>("eventId");
    public static DataField<uint> MachinePid { get; } = new DataField<uint>("machinePid");
    public static DataField<uint> Vcpu { get; } = new DataField<uint>("vcpu");

    /// <summary>
    /// Gets the complete schema for TraceSampleEntry.
    /// </summary>
    public static ParquetSchema Schema { get; } = new ParquetSchema(
        Id, PerfId, Pid, Tid, Time, Cpu, Flags, Ip, Addr, Period, 
        InsnCnt, CycCnt, Weight, Cpumode, AddrCorrelatesSym, EventId, MachinePid, Vcpu
    );
}