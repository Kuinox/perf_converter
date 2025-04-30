namespace PerfConverter.Persistance;

public interface IBatchPersistance<T> : IAsyncDisposable
{
    Task PersistAsync(IReadOnlyCollection<T> batch);
}