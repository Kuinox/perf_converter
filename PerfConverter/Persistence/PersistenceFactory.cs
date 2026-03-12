using PerfConverter.Entry;
using PerfConverter.Persistence.ParquetDotNet;

namespace PerfConverter.Persistence;

public static class PersistenceFactory
{
    const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    public static ParquetPersistenceLifetime CreatePersistence()
    {
        // Target ~1GB row groups. TraceEntry is ~250 bytes/row.
        // At 750k events/sec, flush every ~7 seconds instead of every 0.7s
        var batchSize = 5_000_000;
        string? batchSizeEnv = Environment.GetEnvironmentVariable("BATCH_SIZE");
        if (!string.IsNullOrEmpty(batchSizeEnv) && int.TryParse(batchSizeEnv, out int parsedBatchSize))
        {
            batchSize = parsedBatchSize;
            Console.Error.WriteLine($"Using batch size of {batchSize}");
        }
        var outputDirectory = Environment.GetEnvironmentVariable(OUTPUT_DIRECTORY_ENV) ?? "parquet_output";
        Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
        return ParquetPersistenceLifetime.Create(outputDirectory, batchSize);
    }

    public static (Func<string, IPersister<TraceEntry>>, Func<string, IPersister<StackRange>>) CreateDualPersistenceFactories()
    {
        var persistenceLifetime = CreatePersistence();
        return (persistenceLifetime.CreateTraceBatcher, persistenceLifetime.CreateStackRangeBatcher);
    }
}
