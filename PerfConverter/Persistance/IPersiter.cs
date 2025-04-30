namespace PerfConverter.Persistance;

public interface IPersiter<T> : IAsyncDisposable
{
    void Persit(T val);
}
