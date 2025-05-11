namespace Temp.Core;

public interface IPersister<T> : IAsyncDisposable
{
    void Persist(T val);
}
