using Parquet.Data;
using Parquet.Schema;

namespace Temp.Schema;

/// <summary>
/// Provides Parquet schema for AddressEntry.
/// </summary>
public static class AddressSchema
{
    public static DataField<ulong> Id { get; } = new DataField<ulong>("id");
    public static DataField<ulong> TraceId { get; } = new DataField<ulong>("traceId");
    public static DataField<ulong> Address { get; } = new DataField<ulong>("address");
    public static DataField<uint> Pid { get; } = new DataField<uint>("pid");
    public static DataField<bool> IsIp { get; } = new DataField<bool>("isIp");
    public static DataField<uint> Size { get; } = new DataField<uint>("size");
    public static DataField<uint> Symoff { get; } = new DataField<uint>("symoff");
    public static DataField<ulong> SymStrId { get; } = new DataField<ulong>("symStrId");
    public static DataField<ulong> SymStart { get; } = new DataField<ulong>("symStart");
    public static DataField<ulong> SymEnd { get; } = new DataField<ulong>("symEnd");
    public static DataField<ulong> DsoStrId { get; } = new DataField<ulong>("dsoStrId");
    public static DataField<byte> SymBinding { get; } = new DataField<byte>("symBinding");
    public static DataField<byte> Is64Bit { get; } = new DataField<byte>("is64Bit");
    public static DataField<byte> IsKernelIp { get; } = new DataField<byte>("isKernelIp");
    public static DataField<byte[]> BuildId { get; } = new DataField<byte[]>("buildId");
    public static DataField<byte> Filtered { get; } = new DataField<byte>("filtered");
    public static DataField<ulong> CommStrId { get; } = new DataField<ulong>("commStrId");
    public static DataField<ulong> Priv { get; } = new DataField<ulong>("priv");

    /// <summary>
    /// Gets the complete schema for AddressEntry.
    /// </summary>
    public static ParquetSchema Schema { get; } = new ParquetSchema(
        Id, TraceId, Address, Pid, IsIp, Size, Symoff, SymStrId, SymStart, SymEnd,
        DsoStrId, SymBinding, Is64Bit, IsKernelIp, BuildId, Filtered, CommStrId, Priv
    );
}