using System.Runtime.InteropServices;

namespace Temp.Schema.Entry;

public sealed class EntryContentPool
{
    internal const int StrongRetentionIterations = 500;

    public static EntryContentPool Shared { get; } = new();

    readonly Dictionary<int, List<StringEntry>> _stringEntries = new();
    readonly Dictionary<int, List<ByteEntry>> _byteEntries = new();
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
        var iteration = _currentIteration;

        if (!_byteEntries.TryGetValue(hash, out var bucket))
        {
            bucket = new List<ByteEntry>();
            _byteEntries[hash] = bucket;
        }

        for (var i = 0; i < bucket.Count; i++)
        {
            if (bucket[i].TryGet(source, iteration, out var value))
                return value;
        }

        var newArray = source.ToArray();
        bucket.Add(new ByteEntry(newArray, iteration));
        return newArray;
    }

    public void Tick()
    {
        _currentIteration++;
        Trim(_stringEntries, _currentIteration);
        Trim(_byteEntries, _currentIteration);

        if (_currentIteration % 1000 == 0)
        {
            var stringCount = 0;
            foreach (var bucket in _stringEntries.Values)
                stringCount += bucket.Count;
            var byteCount = 0;
            foreach (var bucket in _byteEntries.Values)
                byteCount += bucket.Count;
            Console.Error.WriteLine($"POOL_SIZE|Iteration={_currentIteration}|Strings={stringCount:N0}|ByteArrays={byteCount:N0}");
        }
    }

    string GetOrAddString(ReadOnlySpan<byte> bytes, nint ptr)
    {
        var hash = GetHash(bytes);
        var iteration = _currentIteration;

        if (!_stringEntries.TryGetValue(hash, out var bucket))
        {
            bucket = new List<StringEntry>();
            _stringEntries[hash] = bucket;
        }

        for (var i = 0; i < bucket.Count; i++)
        {
            if (bucket[i].TryGet(bytes, iteration, out var value))
                return value;
        }

        var created = Marshal.PtrToStringUTF8(ptr)!;
        bucket.Add(new StringEntry(bytes.ToArray(), created, iteration));
        return created;
    }

    static int GetHash(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    static void Trim<TEntry>(Dictionary<int, List<TEntry>> dictionary, int iteration)
        where TEntry : IPoolEntry
    {
        List<int>? keysToRemove = null;
        foreach (var kvp in dictionary)
        {
            var bucket = kvp.Value;
            for (var i = bucket.Count - 1; i >= 0; i--)
            {
                var entry = bucket[i];
                entry.DemoteIfNeeded(iteration);
                if (entry.IsExpired())
                    bucket.RemoveAt(i);
            }

            if (bucket.Count == 0)
            {
                keysToRemove ??= new List<int>();
                keysToRemove.Add(kvp.Key);
            }
        }

        if (keysToRemove != null)
        {
            foreach (var key in keysToRemove)
                dictionary.Remove(key);
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
