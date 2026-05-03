using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence;
using System.Runtime.InteropServices;
using Temp.Schema.Entry;

namespace PerfConverter;

class ThreadProcessor(Func<string, ITracePersister> tracePersistenceFactory) : IDisposable
{
    readonly Dictionary<ReadOnlyMemory<byte>, ITracePersister> _eventMapping = [];
    readonly List<ITracePersister> _tracePersisters = [];

    ulong _currentEntryId = 1;

    public unsafe void ProcessData(
        PerfDlFilterSample* sample,
        PerfDlfilterAl* ip,
        PerfDlfilterAl* address,
        ulong ipLocationId,
        ulong addressLocationId,
        ReadOnlyMemory<byte>? srcFilePath,
        uint lineNumber)
    {
        var @event = EntryContentPool.Shared.GetByteMemoryFromNullTerminatedPtr((nint)sample->@event);
        ref var processor = ref CollectionsMarshal.GetValueRefOrAddDefault(_eventMapping, @event, out var exists);
        if (!exists)
        {
            var eventName = GetEventFileComponent(@event.Span);
            var traceKey = $"pid={sample->pid}/tid={sample->tid}/{eventName}.parquet";
            var tracePersister = tracePersistenceFactory(traceKey);
            _tracePersisters.Add(tracePersister);

            processor = tracePersister;
        }

        var entryId = _currentEntryId++;
        processor!.Persist(entryId, sample, ip, address, ipLocationId, addressLocationId, srcFilePath, lineNumber, @event);
    }

    public void Dispose()
    {
        foreach (var persister in _tracePersisters)
            persister.Dispose();
    }

    static string GetEventFileComponent(ReadOnlySpan<byte> eventBytes)
    {
        var colonIndex = eventBytes.IndexOf((byte)':');
        if (colonIndex >= 0)
            eventBytes = eventBytes[..colonIndex];

        if (eventBytes.IsEmpty)
            return "unknown";

        var allSafeAscii = true;
        for (var i = 0; i < eventBytes.Length; i++)
        {
            var b = eventBytes[i];
            if (!(b is >= (byte)'a' and <= (byte)'z' ||
                  b is >= (byte)'A' and <= (byte)'Z' ||
                  b is >= (byte)'0' and <= (byte)'9' ||
                  b is (byte)'_' or (byte)'-' or (byte)'.'))
            {
                allSafeAscii = false;
                break;
            }
        }

        if (allSafeAscii)
        {
            Span<char> chars = stackalloc char[eventBytes.Length];
            for (var i = 0; i < eventBytes.Length; i++)
                chars[i] = (char)eventBytes[i];
            return new string(chars);
        }

        return Convert.ToHexString(eventBytes);
    }
}
