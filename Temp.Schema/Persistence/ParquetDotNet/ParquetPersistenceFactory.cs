using Parquet;
using PerfConverter.Entry;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

public static class ParquetPersistenceFactory
{
    public static IPersistenceLifetime CreatePersistence(
        string outputDirectory,
        int batchSize,
        BatchingMode batchingMode,
        CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(outputDirectory);

        return new ParquetPersistenceLifetime((key) =>
        {
            Console.Error.WriteLine($"Creating parquet persistence for {key}");
            var path = Path.Combine(outputDirectory, key);
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            var persister = ParquetTracePersistence.Create(path, compressionMethod).GetAwaiter().GetResult();
            return Batcher<TraceEntry>.Create(persister, batchSize, batchingMode);
        });
    }
}