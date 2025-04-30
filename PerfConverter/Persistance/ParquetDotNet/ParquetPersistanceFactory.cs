using PerfConverter.Entry;
using System.IO;

namespace PerfConverter.Persistance.ParquetDotNet;

public static class ParquetPersistanceFactory
{
    public static IPersistanceLifetime CreatePersistance(
        string outputDirectory, 
        int batchSize, 
        BatchingMode batchingMode)
    {
        Directory.CreateDirectory(outputDirectory);
        
        var symbolPersister = ParquetSymPersistance.Create(outputDirectory, batchSize);
        var addressPersister = ParquetAddressPersistance.Create(outputDirectory, batchSize);
        var tracePersister = ParquetTracePersistance.Create(outputDirectory, batchSize);
        
        var symbolBatcher = Batcher<SymbolEntry>.Create(symbolPersister, batchSize, batchingMode);
        var addressBatcher = Batcher<AddressEntry>.Create(addressPersister, batchSize, batchingMode);
        var traceBatcher = Batcher<TraceSampleEntry>.Create(tracePersister, batchSize, batchingMode);
        
        return new ParquetPersistanceLifetime(symbolBatcher, addressBatcher, traceBatcher);
    }
}