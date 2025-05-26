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

        var symbolPersister = ParquetStringPersistence.Create(outputDirectory, "symbols.parquet", compressionMethod).GetAwaiter().GetResult();
        var commPersister = ParquetStringPersistence.Create(outputDirectory, "comms.parquet", compressionMethod).GetAwaiter().GetResult();
        var eventPersister = ParquetStringPersistence.Create(outputDirectory, "events.parquet", compressionMethod).GetAwaiter().GetResult();
        var dsoPersister = ParquetStringPersistence.Create(outputDirectory, "dso.parquet", compressionMethod).GetAwaiter().GetResult();

        var addressPersister = ParquetAddressPersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        var symbolBatcher = Batcher<StringEntry>.Create(symbolPersister, batchSize, batchingMode);
        var commBatcher = Batcher<StringEntry>.Create(commPersister, batchSize, batchingMode);
        var eventBatcher = Batcher<StringEntry>.Create(eventPersister, batchSize, batchingMode);
        var dsoBatcher = Batcher<StringEntry>.Create(eventPersister, batchSize, batchingMode); ;
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);


        return new ParquetPersistenceLifetime(symbolBatcher, commBatcher, eventBatcher, dsoBatcher, addressBatcher, (key) =>
        {
            Console.Error.WriteLine($"Creating parquet persistence for {key}");
            var subDir = Path.Combine(outputDirectory, key);
            var persister = ParquetTracePersistence.Create(subDir, compressionMethod).GetAwaiter().GetResult();
            return Batcher<TraceSampleEntry>.Create(persister, batchSize, batchingMode);
        });
    }
}