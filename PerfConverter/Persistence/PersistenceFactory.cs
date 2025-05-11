using Parquet;
using PerfConverter.Persistence.ParquetDotNet;
using PerfConverter.Persistence.Sql;

namespace PerfConverter.Persistence;

public static class PersistenceFactory
{
    const string PERSISTENCE_TYPE_ENV = "PERSISTENCE_TYPE";
    const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    const string CONNECTION_STRING_ENV = "DB_CONNECTION_STRING";
    const string PARQUET_COMPRESS_ENV = "PARQUET_COMPRESSION";
    public static IPersistenceLifetime CreatePersistence()
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

        if (persistenceType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Warning: Converting to SQLite is extremely slow due to lack of data compression.");
            string connectionString = Environment.GetEnvironmentVariable(CONNECTION_STRING_ENV) ?? "Data Source=perf.db";
            Console.Error.WriteLine($"Using SQLite connection: {connectionString}");
            return SqlPersistenceFactory.CreatePersistence(connectionString, batchSize, BatchingMode.ASAP);
        }

        var compressionMethod = CompressionMethod.Snappy;
        if (!string.IsNullOrEmpty(compressionEnv))
        {
            if (Enum.TryParse<CompressionMethod>(compressionEnv, true, out var parsedCompression))
                compressionMethod = parsedCompression;
            else
                Console.Error.WriteLine($"Invalid Parquet compression method: {compressionEnv}. Defaulting to Snappy.");
        }
        Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
        return ParquetPersistenceFactory.CreatePersistence(outputDirectory, batchSize, BatchingMode.OnFull, compressionMethod);
    }
}