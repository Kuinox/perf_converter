namespace Temp.Core;

public interface IBatchPersistence<T> : IAsyncDisposable
{
    Task PersistAsync(IReadOnlyCollection<T> batch);
}