namespace PerfConverter.Persistance;

public interface IBatchPersistance<T>
{
    Task PersistAsync(IReadOnlyCollection<T> batch);
}
