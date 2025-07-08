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
            var dir = Path.Combine(outputDirectory, key); 
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, "tracesamples.parquet");
            var persister = ParquetTracePersistence.Create(filePath, compressionMethod).GetAwaiter().GetResult();
            return Batcher<TraceEntry>.Create(persister, batchSize, batchingMode);
        });
    }
}