namespace PerfConverter.Persistence;

public interface IPersister<T> : IAsyncDisposable
{
    void Persist(T val);
}
