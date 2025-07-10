using Parquet;
using PerfConverter.Entry;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter.Persistence.ParquetDotNet;

/// <summary>
/// Manages the lifetime of Parquet persistence components
/// </summary>
public class ParquetPersistenceLifetime(Func<string, Batcher<TraceEntry>> traceBatcherFactory) : IAsyncDisposable
{
    readonly Dictionary<string, Batcher<TraceEntry>> _tracePersister = [];

    public IPersister<TraceEntry> CreateTraceBatcher(string key)
    {
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_tracePersister, key, out _);
        persistence ??= traceBatcherFactory(key);
        return persistence;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _tracePersister.Values)
        {
            await entry.DisposeAsync();
        }
    }

    public static ParquetPersistenceLifetime Create(
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
            var dir = Path.GetDirectoryName(path)!; // key can be a path.
            Directory.CreateDirectory(dir);
            var persister = ParquetTracePersistence.Create(path, compressionMethod).GetAwaiter().GetResult();
            return Batcher<TraceEntry>.Create(persister, batchSize, batchingMode);
        });
    }
}