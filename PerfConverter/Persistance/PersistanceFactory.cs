using PerfConverter.Entry;
using PerfConverter.Persistance.ParquetDotNet;
using PerfConverter.Persistance.Sql;
using System;

namespace PerfConverter.Persistance;

public static class PersistanceFactory
{
    private const string PERSISTENCE_TYPE_ENV = "PERSISTENCE_TYPE";
    private const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    private const string CONNECTION_STRING_ENV = "DB_CONNECTION_STRING";

    public static IPersistanceLifetime CreatePersistance(int batchSize)
    {
        var persistenceType = Environment.GetEnvironmentVariable(PERSISTENCE_TYPE_ENV) ?? string.Empty;

        if (persistenceType.Equals("parquetdotnet", StringComparison.OrdinalIgnoreCase))
        {
            string outputDirectory = Environment.GetEnvironmentVariable(OUTPUT_DIRECTORY_ENV) ?? "parquet_output";
            Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
            return ParquetPersistanceFactory.CreatePersistance(outputDirectory, batchSize, BatchingMode.OnFull);
        }

        // Default to SQLite
        string connectionString = Environment.GetEnvironmentVariable(CONNECTION_STRING_ENV) ?? "Data Source=perf.db";
        Console.Error.WriteLine($"Using SQLite connection: {connectionString}");
        return SqlPersistanceFactory.CreatePersistance(connectionString, batchSize, BatchingMode.ASAP);
    }
}