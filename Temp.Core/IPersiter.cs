namespace PerfConverter.Persistence;

public interface IPersiter<T> : IAsyncDisposable
{
    void Persit(T val);
}
