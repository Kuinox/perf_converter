using Parquet;
using PerfConverter.Entry;

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
        
        var symbolPersister = ParquetSymPersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        var addressPersister = ParquetAddressPersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        var tracePersister = ParquetTracePersistence.Create(outputDirectory, compressionMethod).GetAwaiter().GetResult();
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new ParquetPersistenceLifetime(symbolBatcher, addressBatcher, traceBatcher);
    }
}