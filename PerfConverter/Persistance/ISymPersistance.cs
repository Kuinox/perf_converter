namespace PerfConverter.Persistance
{
    public interface ISymPersistance
    {
        void Persist(IReadOnlyCollection<SymbolEntry> batch);
    }
}
