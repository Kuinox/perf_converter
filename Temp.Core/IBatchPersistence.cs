namespace PerfConverter.Persistence;

public interface IBatchPersistence<T> : IAsyncDisposable
{
    Task PersistAsync(IReadOnlyCollection<T> batch);
}