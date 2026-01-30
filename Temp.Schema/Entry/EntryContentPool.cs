using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Temp.Schema.Entry;

public sealed class EntryContentPool
{
    internal const int StrongRetentionIterations = 500;

    public static EntryContentPool Shared { get; } = new();

    readonly ConcurrentDictionary<int, List<StringEntry>> _stringEntries = new();
    readonly ConcurrentDictionary<int, List<ByteEntry>> _byteEntries = new();
    int _currentIteration;

    EntryContentPool() { }

    public unsafe string GetStringFromUtf8Ptr(nint ptr)
    {
        if (ptr == 0) return string.Empty;

        var bytePtr = (byte*)ptr;
        var length = 0;
        while (bytePtr[length] != 0) length++;

        if (length == 0) return string.Empty;

        var bytes = new ReadOnlySpan<byte>(bytePtr, length);
        return GetOrAddString(bytes, ptr);
    }

    public byte[] GetByteArray(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return Array.Empty<byte>();

        var hash = GetHash(source);
        var iteration = Volatile.Read(ref _currentIteration);
        var bucket = _byteEntries.GetOrAdd(hash, static _ => new List<ByteEntry>());

        lock (bucket)
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].TryGet(source, iteration, out var value))
                {
                    return value;
                }
            }

            var newArray = source.ToArray();
            bucket.Add(new ByteEntry(newArray, iteration));
            return newArray;
        }
    }

    public void Tick()
    {
        var iteration = Interlocked.Increment(ref _currentIteration);
        Trim(_stringEntries, iteration);
        Trim(_byteEntries, iteration);

        // Report pool sizes every 1000 ticks
        if (iteration % 1000 == 0)
        {
            var stringCount = _stringEntries.Values.Sum(bucket => bucket.Count);
            var byteCount = _byteEntries.Values.Sum(bucket => bucket.Count);
            Console.Error.WriteLine($"POOL_SIZE|Iteration={iteration}|Strings={stringCount:N0}|ByteArrays={byteCount:N0}");
        }
    }

    string GetOrAddString(ReadOnlySpan<byte> bytes, nint ptr)
    {
        var hash = GetHash(bytes);
        var iteration = Volatile.Read(ref _currentIteration);
        var bucket = _stringEntries.GetOrAdd(hash, static _ => new List<StringEntry>());

        lock (bucket)
        {
            for (var i = 0; i < bucket.Count; i++)
            {
                if (bucket[i].TryGet(bytes, iteration, out var value))
                {
                    return value;
                }
            }

            var created = Marshal.PtrToStringUTF8(ptr)!;
            bucket.Add(new StringEntry(bytes.ToArray(), created, iteration));
            return created;
        }
    }

    static int GetHash(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    static void Trim<TEntry>(ConcurrentDictionary<int, List<TEntry>> dictionary, int iteration)
        where TEntry : IPoolEntry
    {
        foreach (var kvp in dictionary)
        {
            var bucket = kvp.Value;
            lock (bucket)
            {
                for (var i = bucket.Count - 1; i >= 0; i--)
                {
                    var entry = bucket[i];
                    entry.DemoteIfNeeded(iteration);
                    if (entry.IsExpired())
                    {
                        bucket.RemoveAt(i);
                    }
                }

                if (bucket.Count == 0)
                {
                    dictionary.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    interface IPoolEntry
    {
        void DemoteIfNeeded(int iteration);
        bool IsExpired();
    }

    sealed class StringEntry : IPoolEntry
    {
        readonly byte[] _bytes;
        string? _strongRef;
        WeakReference<string>? _weakRef;
        int _lastAccessIteration;

        public StringEntry(byte[] bytes, string value, int iteration)
        {
            _bytes = bytes;
            _strongRef = value;
            _lastAccessIteration = iteration;
        }

        public bool TryGet(ReadOnlySpan<byte> bytes, int iteration, out string value)
        {
            if (!_bytes.AsSpan().SequenceEqual(bytes))
            {
                value = default!;
                return false;
            }

            var current = _strongRef;
            if (current is null)
            {
                if (_weakRef is null || !_weakRef.TryGetTarget(out current))
                {
                    value = default!;
                    return false;
                }
                _strongRef = current;
            }

            _lastAccessIteration = iteration;
            value = current;
            return true;
        }

        public void DemoteIfNeeded(int iteration)
        {
            if (_strongRef is null) return;
            if (iteration - _lastAccessIteration < StrongRetentionIterations) return;

            _weakRef ??= new WeakReference<string>(_strongRef);
            _strongRef = null;
        }

        public bool IsExpired()
        {
            if (_strongRef is not null) return false;
            if (_weakRef is null) return true;
            return !_weakRef.TryGetTarget(out _);
        }
    }

    sealed class ByteEntry(byte[] bytes, int iteration) : IPoolEntry
    {
        byte[]? _strongRef = bytes;
        WeakReference<byte[]>? _weakRef;
        int _lastAccessIteration = iteration;

        public bool TryGet(ReadOnlySpan<byte> bytes, int iteration, out byte[] value)
        {
            var current = _strongRef;
            if (current is not null && current.AsSpan().SequenceEqual(bytes))
            {
                _lastAccessIteration = iteration;
                value = current;
                return true;
            }

            if (_weakRef is not null && _weakRef.TryGetTarget(out current) && current.AsSpan().SequenceEqual(bytes))
            {
                _strongRef = current;
                value = current;
                return true;
            }

            value = default!;
            return false;
        }

        public void DemoteIfNeeded(int iteration)
        {
            if (_strongRef is null) return;
            if (iteration - _lastAccessIteration < StrongRetentionIterations) return;

            _weakRef ??= new WeakReference<byte[]>(_strongRef);
            _strongRef = null;
        }

        public bool IsExpired()
        {
            if (_strongRef is not null) return false;
            if (_weakRef is null) return true;
            return !_weakRef.TryGetTarget(out _);
        }
    }
}