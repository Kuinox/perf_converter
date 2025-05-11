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
        var addressPersister = ParquetAddressPersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        var tracePersister = ParquetTracePersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        
        var symbolBatcher = Batcher<StringEntry>.Create(symbolPersister, batchSize, batchingMode);
        var commBatcher = Batcher<StringEntry>.Create(commPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new ParquetPersistenceLifetime(symbolBatcher, commBatcher, addressBatcher, traceBatcher);
    }
}