using Parquet;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetDotNet;

public static class ParquetPersistanceFactory
{
    public static IPersistanceLifetime CreatePersistance(
        string outputDirectory, 
        int batchSize, 
        BatchingMode batchingMode,
        CompressionMethod compressionMethod)
    {
        Directory.CreateDirectory(outputDirectory);
        
        var symbolPersister = ParquetSymPersistance.Create(outputDirectory, batchSize, compressionMethod).GetAwaiter().GetResult();
        var addressPersister = ParquetAddressPersistance.Create(outputDirectory, batchSize, compressionMethod).GetAwaiter().GetResult();
        var tracePersister = ParquetTracePersistance.Create(outputDirectory, batchSize, compressionMethod).GetAwaiter().GetResult();
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new ParquetPersistanceLifetime(symbolBatcher, addressBatcher, traceBatcher);
    }
}