using PerfConverter.Entry;
using PerfConverter.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfConverter.Processor;

class CommProcessor(IPersister<CommEntry> persistence) : ICommProcessor
{
    readonly Dictionary<string, ulong> _ids = new();
    public ulong Process(string comm)
    {
        var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, comm, out var exists);
        if (!exists)
        {
            defaultEntry = (ulong)_ids.Count;
            persistence.Persist(new CommEntry { Id = defaultEntry, Comm = comm });
        }
        return defaultEntry;
    }
}
