using PerfConverter.Entry;
using PerfConverter.Persistence;
using System.Runtime.InteropServices;

namespace PerfConverter.Processor;

public class SymProcessor(IPersister<SymbolEntry> persistence) : ISymProcessor
{
    readonly Dictionary<string, ulong> _ids = [];

    public ulong Process(string sym)
    {
        var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, sym, out var exists);
        if (!exists)
        {
            defaultEntry = (ulong)_ids.Count;
            persistence.Persist(new SymbolEntry { Id = defaultEntry, Symbol = sym });
        }
        return defaultEntry;
    }
}
