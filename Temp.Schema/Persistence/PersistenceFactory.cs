using Parquet;
using PerfConverter.Persistence.ParquetDotNet;
using Temp.Core;

namespace PerfConverter.Persistence;

public static class PersistenceFactory
{
    const string PERSISTENCE_TYPE_ENV = "PERSISTENCE_TYPE";
    const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    const string PARQUET_COMPRESS_ENV = "PARQUET_COMPRESSION";
    public static ParquetPersistenceLifetime CreatePersistence()
    {
        var batchSize = 10_000_000;
        string? batchSizeEnv = Environment.GetEnvironmentVariable("BATCH_SIZE");
        if (!string.IsNullOrEmpty(batchSizeEnv) && int.TryParse(batchSizeEnv, out int parsedBatchSize))
        {
            batchSize = parsedBatchSize;
            Console.Error.WriteLine($"Using batch size of {batchSize}");
        }
        var persistenceType = Environment.GetEnvironmentVariable(PERSISTENCE_TYPE_ENV) ?? string.Empty;
        var outputDirectory = Environment.GetEnvironmentVariable(OUTPUT_DIRECTORY_ENV) ?? "parquet_output";
        var compressionEnv = Environment.GetEnvironmentVariable(PARQUET_COMPRESS_ENV);

        // we used to support sqlite but it was way too slow to be useful, due to lack of compression.

        var compressionMethod = CompressionMethod.Gzip; // snappy bugged: https://github.com/aloneguid/parquet-dotnet/issues/393
        if (!string.IsNullOrEmpty(compressionEnv))
        {
            if (Enum.TryParse<CompressionMethod>(compressionEnv, true, out var parsedCompression))
                compressionMethod = parsedCompression;
            else
                Console.Error.WriteLine($"Invalid Parquet compression method: {compressionEnv}. Defaulting to Snappy.");
        }
        Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
        return ParquetPersistenceLifetime.Create(outputDirectory, batchSize, BatchingMode.OnFull, compressionMethod);
    }
}