using Plank.Writing;

namespace PerfConverter.Persistence.Plank;

static class ParquetPersistenceOptions
{
    internal static ParquetWriterOptions WriterOptions { get; } = new()
    {
        Compression = CompressionKind.Snappy
    };
}
