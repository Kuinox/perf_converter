using PerfConverter.Entry;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter.Processor;

public class StringProcessor(IPersister<StringEntry> persistence) : IStringProcessor
{
    readonly Dictionary<string, ulong> _ids = [];

    public ulong Process(string sym)
    {
        var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, sym, out var exists);
        if (!exists)
        {
            defaultEntry = (ulong)_ids.Count;
            persistence.Persist(new StringEntry { Id = defaultEntry, Symbol = sym });
        }
        return defaultEntry;
    }
}
