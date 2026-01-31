using Parquet;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Schema;

public class StackRangeSchema
{
    static readonly ColumnEncodingOptions DeltaOnly = new() { UseDeltaBinaryPackedEncoding = true, UseDictionaryEncoding = false };

    public StackRangeSchema()
    {
        Schema = new ParquetSchema(
            StartTrace.Field,
            EndTrace.Field
        );
    }

    // Trace IDs are sequential - delta encoding
    public ParquetColumn<ulong> StartTrace { get; } = new("startTrace", DeltaOnly);
    public ParquetColumn<ulong> EndTrace { get; } = new("endTrace", DeltaOnly);

    public ParquetSchema Schema { get; }

    public async Task Writer(ParquetWriter writer)
    {
        using var groupWriter = writer.CreateRowGroup();
        await StartTrace.Write(groupWriter);
        await EndTrace.Write(groupWriter);
    }

    public async IAsyncEnumerable<StackRange> ReadAll(ParquetReader reader)
    {
        foreach (var groupReader in reader.RowGroups)
            await foreach (var entry in ReadRowGroup(groupReader))
                yield return entry;
    }

    public async IAsyncEnumerable<StackRange> ReadRowGroup(IParquetRowGroupReader groupReader)
    {
        var startTrace = await groupReader.ReadColumnAsync(StartTrace.Field);
        var endTrace = await groupReader.ReadColumnAsync(EndTrace.Field);

        for (var i = 0; i < startTrace.Data.Length; i++)
        {
            yield return new StackRange()
            {
                StartTrace = startTrace.AsSpan<ulong>()[i],
                EndTrace = endTrace.AsSpan<ulong>()[i],
            };
        }
    }

    public void Resize(int newSize)
    {
        StartTrace.Resize(newSize);
        EndTrace.Resize(newSize);
    }
}
