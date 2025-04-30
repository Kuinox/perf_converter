using ParquetSharp;
using PerfConverter.Entry;

namespace PerfConverter.Persistance.ParquetSharp;

public static class ParquetSharpPersistanceFactory
{
    public static IPersistanceLifetime CreatePersistance(
        string outputDirectory, 
        int batchSize, 
        BatchingMode batchingMode,
        Compression compressionMethod)
    {
        Directory.CreateDirectory(outputDirectory);
        
        var symbolPersister = ParquetSharpSymPersistance.Create(outputDirectory, batchSize, compressionMethod);
        var addressPersister = ParquetSharpAddressPersistance.Create(outputDirectory, batchSize, compressionMethod);
        var tracePersister = ParquetSharpTracePersistance.Create(outputDirectory, batchSize, compressionMethod);
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new ParquetSharpPersistanceLifetime(symbolBatcher, addressBatcher, traceBatcher);
    }
}