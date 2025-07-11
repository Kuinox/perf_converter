using Parquet;
using Parquet.Schema;
using PerfConverter.Entry;

namespace PerfConverter.Schema;

public class StackRangeSchema
{
    public StackRangeSchema()
    {
        Schema = new ParquetSchema(
            StartTrace.Field,
            EndTrace.Field
        );
    }

    public ParquetColumn<long> StartTrace { get; } = new("startTrace");
    public ParquetColumn<long> EndTrace { get; } = new("endTrace");

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
                StartTrace = startTrace.AsSpan<long>()[i],
                EndTrace = endTrace.AsSpan<long>()[i],
            };
        }
    }

    public void Resize(int newSize)
    {
        StartTrace.Resize(newSize);
        EndTrace.Resize(newSize);
    }
}
