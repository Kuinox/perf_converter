namespace PerfConverter.Persistance;

public interface IAddressPersistance
{
    void Persist(IReadOnlyCollection<AddressEntry> batch);
}
