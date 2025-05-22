using Parquet.Data;
using Parquet.Schema;

namespace PerfConverter.Persistence.ParquetDotNet.Schemas;

/// <summary>
/// Provides Parquet schema for dictionary entries.
/// </summary>
public static class DictionarySchema
{
    public static DataField<ulong> Id { get; } = new DataField<ulong>("id");
    public static DataField<string> Symbol { get; } = new DataField<string>("symbol");

    /// <summary>
    /// Gets the complete schema for dictionary entries.
    /// </summary>
    public static ParquetSchema Schema { get; } = new ParquetSchema(Id, Symbol);
}