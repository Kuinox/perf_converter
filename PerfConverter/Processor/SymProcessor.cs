using PerfConverter.Entry;
using PerfConverter.Persistence;
using System.Runtime.InteropServices;

namespace PerfConverter.Processor;

public class SymProcessor(IPersiter<SymbolEntry> persistence) : ISymProcessor
{
    readonly Dictionary<string, long> _ids = [];

    public long Process(string sym)
    {
        var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, sym, out var exists);
        if (!exists)
        {
            defaultEntry = _ids.Count;
            persistence.Persit(new SymbolEntry { Id = defaultEntry, Symbol = sym });
        }
        return defaultEntry;
    }
}
