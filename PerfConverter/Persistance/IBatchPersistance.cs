namespace PerfConverter.Persistance;

public interface IBatchPersistance<T>
{
    void Persist(IReadOnlyCollection<T> batch);
}
