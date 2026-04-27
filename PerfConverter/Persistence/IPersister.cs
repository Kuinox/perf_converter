namespace PerfConverter.Persistence;

public interface IPersister<T> : IDisposable
{
    void Persist(T val);
}
