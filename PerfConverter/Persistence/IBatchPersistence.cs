namespace PerfConverter.Persistence;

public interface IBatchPersistence<T> : IDisposable
{
    void Persist(IReadOnlyCollection<T> batch);
}
