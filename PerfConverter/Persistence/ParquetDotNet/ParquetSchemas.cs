using Parquet.Data;
using Parquet.Schema;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Provides Parquet schemas for the different entry types used in the application.
/// </summary>
public static class ParquetSchemas
{
    /// <summary>
    /// Gets the schema for TraceSampleEntry.
    /// </summary>
    public static ParquetSchema TraceSampleSchema => new ParquetSchema(
        new DataField<ulong>("id"),
        new DataField<ulong>("perfId"),
        new DataField<uint>("pid"),
        new DataField<uint>("tid"),
        new DataField<ulong>("time"),
        new DataField<uint>("cpu"),
        new DataField<uint>("flags"),
        new DataField<ulong>("ip"),
        new DataField<ulong>("addr"),
        new DataField<ulong>("period"),
        new DataField<ulong>("insnCnt"),
        new DataField<ulong>("cycCnt"),
        new DataField<ulong>("weight"),
        new DataField<byte>("cpumode"),
        new DataField<byte>("addrCorrelatesSym"),
        new DataField<string>("event"),
        new DataField<uint>("machinePid"),
        new DataField<uint>("vcpu")
    );

    /// <summary>
    /// Gets the schema for AddressEntry.
    /// </summary>
    public static ParquetSchema AddressSchema => new ParquetSchema(
        new DataField<ulong>("id"),
        new DataField<ulong>("traceId"),
        new DataField<ulong>("address"),
        new DataField<uint>("pid"),
        new DataField<bool>("isIp"),
        new DataField<uint>("size"),
        new DataField<uint>("symoff"),
        new DataField<ulong>("symStrId"),
        new DataField<ulong>("symStart"),
        new DataField<ulong>("symEnd"),
        new DataField<ulong>("dso"),
        new DataField<byte>("symBinding"),
        new DataField<byte>("is64Bit"),
        new DataField<byte>("isKernelIp"),
        new DataField<byte[]>("buildId"),
        new DataField<byte>("filtered"),
        new DataField<ulong>("commStrId"),
        new DataField<ulong>("priv")
    );

    /// <summary>
    /// Gets the schema for StringEntry.
    /// </summary>
    public static ParquetSchema StringSchema => new ParquetSchema(
        new DataField<ulong>("id"),
        new DataField<string>("symbol")
    );

    /// <summary>
    /// Gets the schema for AuxDataLostEntry.
    /// </summary>
    public static ParquetSchema AuxDataLostSchema => new ParquetSchema(
        new DataField<ulong>("time"),
        new DataField<ulong>("pid"),
        new DataField<ulong>("tid"),
        new DataField<ulong>("cpu"),
        new DataField<ulong>("flags")
    );
}