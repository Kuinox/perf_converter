using Parquet;
using PerfConverter.Persistance.ParquetDotNet;
using PerfConverter.Persistance.Sql;

namespace PerfConverter.Persistance;

public static class PersistanceFactory
{
    const string PERSISTENCE_TYPE_ENV = "PERSISTENCE_TYPE";
    const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    const string CONNECTION_STRING_ENV = "DB_CONNECTION_STRING";
    const string PARQUET_COMPRESS_ENV = "PARQUET_COMPRESSION";
    public static IPersistanceLifetime CreatePersistance(int batchSize)
    {
        var persistenceType = Environment.GetEnvironmentVariable(PERSISTENCE_TYPE_ENV) ?? string.Empty;

        if (persistenceType.Equals("parquetdotnet", StringComparison.OrdinalIgnoreCase))
        {
            var compressionMethod = CompressionMethod.Snappy;
            var compressionEnv = Environment.GetEnvironmentVariable(PARQUET_COMPRESS_ENV);
            if (!string.IsNullOrEmpty(compressionEnv))
            {
                if (Enum.TryParse<CompressionMethod>(compressionEnv, true, out var parsedCompression))
                    compressionMethod = parsedCompression;
                else
                    Console.Error.WriteLine($"Invalid Parquet compression method: {compressionEnv}. Defaulting to Snappy.");
            }
            string outputDirectory = Environment.GetEnvironmentVariable(OUTPUT_DIRECTORY_ENV) ?? "parquet_output";
            Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
            return ParquetPersistanceFactory.CreatePersistance(outputDirectory, batchSize, BatchingMode.OnFull, compressionMethod);
        }

        // Default to SQLite
        string connectionString = Environment.GetEnvironmentVariable(CONNECTION_STRING_ENV) ?? "Data Source=perf.db";
        Console.Error.WriteLine($"Using SQLite connection: {connectionString}");
        return SqlPersistanceFactory.CreatePersistance(connectionString, batchSize, BatchingMode.ASAP);
    }
}