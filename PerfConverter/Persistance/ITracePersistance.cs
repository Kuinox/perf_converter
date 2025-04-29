namespace PerfConverter.Persistance;

public interface ITracePersistance
{
    void Persist(IReadOnlyCollection<TraceSample> batch);
}
