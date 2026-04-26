using Plank.Schema;
using Plank.Writing;

namespace PerfConverter.Schema;

public sealed class PlankParquetFileWriter
{
    readonly ParquetWriter _writer;

    PlankParquetFileWriter(ParquetWriter writer)
    {
        _writer = writer;
    }

    internal ParquetWriter Writer => _writer;

    internal static PlankParquetFileWriter Create(Stream stream, ParquetSchema schema)
        => new(schema.CreateWriter(stream));

    public void CloseFile()
        => _writer.CloseFile();
}
