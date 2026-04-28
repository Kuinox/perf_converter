namespace PerfConverter.Persistence;

public interface IPersister<T> : IDisposable
{
    void Persist(T val);
}

public unsafe interface ITracePersister : IDisposable
{
    void Persist(
        ulong entryId,
        PerfConverter.PerfStructs.PerfDlFilterSample* sample,
        PerfConverter.PerfStructs.PerfDlfilterAl* ip,
        PerfConverter.PerfStructs.PerfDlfilterAl* address,
        ReadOnlyMemory<byte>? srcFilePath,
        uint lineNumber,
        ReadOnlyMemory<byte> eventName);
}
