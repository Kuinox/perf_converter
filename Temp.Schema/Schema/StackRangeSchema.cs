using PerfConverter.Entry;
using Plank.Schema;
using Plank.Writing;

namespace PerfConverter.Schema;

public class StackRangeSchema
{
    static readonly ColumnOptions DeltaOnly = new(encodings: [EncodingKind.DeltaBinaryPacked]);

    public StackRangeSchema()
    {
        Schema = new([
            StartTrace.Column,
            EndTrace.Column
        ]);
    }

    public PlankColumn<ulong> StartTrace { get; } = new("startTrace", DeltaOnly);
    public PlankColumn<ulong> EndTrace { get; } = new("endTrace", DeltaOnly);

    public ParquetSchema Schema { get; }

    void WriteTo(ParquetWriter writer)
    {
        var groupWriter = writer.StartRowGroup();
        StartTrace.Write(groupWriter);
        EndTrace.Write(groupWriter);
    }

    public PlankParquetFileWriter CreateWriter(Stream stream)
        => PlankParquetFileWriter.Create(stream, Schema);

    public void Writer(PlankParquetFileWriter writer)
    {
        WriteTo(writer.Writer);
    }

    public void Resize(int newSize)
    {
        StartTrace.Resize(newSize);
        EndTrace.Resize(newSize);
    }
}
