using PerfConverter.Entry;
using PerfConverter.Persistence.Plank;

namespace PerfConverter.Persistence;

public static class PersistenceFactory
{
    const string OUTPUT_DIRECTORY_ENV = "OUTPUT_DIRECTORY";
    public static ParquetPersistenceLifetime CreatePersistence()
    {
        var outputDirectory = Environment.GetEnvironmentVariable(OUTPUT_DIRECTORY_ENV) ?? "parquet_output";
        Console.Error.WriteLine($"Using Parquet output directory: {outputDirectory}");
        return ParquetPersistenceLifetime.Create(outputDirectory);
    }

    public static (Func<string, IPersister<TraceEntry>>, Func<string, IPersister<StackRange>>) CreateDualPersistenceFactories()
    {
        var persistenceLifetime = CreatePersistence();
        return (persistenceLifetime.CreateTraceBatcher, persistenceLifetime.CreateStackRangeBatcher);
    }
}
