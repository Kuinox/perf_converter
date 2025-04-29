namespace PerfConverter.Persistance;

public interface IPersiter<T>
{
    void Persit(T val);
}
