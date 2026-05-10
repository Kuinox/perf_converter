namespace PerfConverter.Persistence;

public interface IPersister<T> : IDisposable
{
    void Persist(T val);
}

public unsafe interface ITracePersister : IDisposable
{
    void Persist(
        ulong entryId,
        PerfConverter.OwnedPerfSample sample,
        PerfConverter.ResolvedLocation ip,
        PerfConverter.ResolvedLocation? address,
        ulong ipLocationId,
        ulong addressLocationId,
        ReadOnlyMemory<byte>? srcFilePath,
        uint lineNumber,
        ReadOnlyMemory<byte> eventName);
}
