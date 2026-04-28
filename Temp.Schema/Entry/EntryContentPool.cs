using System.Buffers;
using System.Runtime.InteropServices;

namespace Temp.Schema.Entry;

public sealed class EntryContentPool : IDisposable
{
    public static EntryContentPool Shared { get; } = new();

    readonly Dictionary<int, List<MemoryEntry>> _entries = new();
    bool _disposed;

    EntryContentPool() { }

    public unsafe ReadOnlyMemory<byte> GetByteMemoryFromNullTerminatedPtr(nint ptr)
    {
        if (ptr == 0)
            return ReadOnlyMemory<byte>.Empty;

        var bytePtr = (byte*)ptr;
        var length = 0;
        while (bytePtr[length] != 0)
            length++;

        if (length == 0)
            return ReadOnlyMemory<byte>.Empty;

        return GetByteMemory(new ReadOnlySpan<byte>(bytePtr, length));
    }

    public unsafe ReadOnlyMemory<byte> CopyByteMemoryFromNullTerminatedPtr(nint ptr)
    {
        if (ptr == 0)
            return ReadOnlyMemory<byte>.Empty;

        var bytePtr = (byte*)ptr;
        var length = 0;
        while (bytePtr[length] != 0)
            length++;

        if (length == 0)
            return ReadOnlyMemory<byte>.Empty;

        return CopyByteMemory(new ReadOnlySpan<byte>(bytePtr, length));
    }

    public unsafe IMemoryOwner<byte>? RentByteMemoryOwnerFromNullTerminatedPtr(nint ptr)
    {
        if (ptr == 0)
            return null;

        var bytePtr = (byte*)ptr;
        var length = 0;
        while (bytePtr[length] != 0)
            length++;

        if (length == 0)
            return null;

        return RentByteMemoryOwner(new ReadOnlySpan<byte>(bytePtr, length));
    }

    public ReadOnlyMemory<byte> GetByteMemory(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return ReadOnlyMemory<byte>.Empty;

        var hash = GetHash(source);
        ref var bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, hash, out _);
        bucket ??= [];

        for (var i = 0; i < bucket.Count; i++)
        {
            if (bucket[i].Matches(source))
                return bucket[i].Memory;
        }

        var entry = MemoryEntry.Create(source);
        bucket.Add(entry);
        return entry.Memory;
    }

    public ReadOnlyMemory<byte> CopyByteMemory(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return ReadOnlyMemory<byte>.Empty;

        var buffer = new byte[source.Length];
        source.CopyTo(buffer);
        return buffer;
    }

    public IMemoryOwner<byte>? RentByteMemoryOwner(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return null;

        var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
        source.CopyTo(buffer);
        return new ArrayPoolMemoryOwner(buffer, source.Length);
    }

    public void Tick()
    {
        if (_entries.Count == 0)
            return;

        if ((_entries.Count & 1023) == 0)
        {
            var entryCount = 0;
            foreach (var bucket in _entries.Values)
                entryCount += bucket.Count;
            Console.Error.WriteLine($"POOL_SIZE|Entries={entryCount:N0}|Buckets={_entries.Count:N0}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var bucket in _entries.Values)
        {
            for (var i = 0; i < bucket.Count; i++)
                bucket[i].Dispose();
        }

        _entries.Clear();
        _disposed = true;
    }

    static int GetHash(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    sealed class MemoryEntry(byte[] buffer, int length) : IDisposable
    {
        readonly byte[] _buffer = buffer;
        readonly int _length = length;
        bool _disposed;

        public ReadOnlyMemory<byte> Memory => new(_buffer, 0, _length);

        public static MemoryEntry Create(ReadOnlySpan<byte> source)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(source.Length);
            source.CopyTo(buffer);
            return new MemoryEntry(buffer, source.Length);
        }

        public bool Matches(ReadOnlySpan<byte> source)
            => source.Length == _length && source.SequenceEqual(_buffer.AsSpan(0, _length));

        public void Dispose()
        {
            if (_disposed)
                return;

            ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
            _disposed = true;
        }
    }

    sealed class ArrayPoolMemoryOwner(byte[] buffer, int length) : IMemoryOwner<byte>
    {
        readonly byte[] _buffer = buffer;
        readonly int _length = length;
        bool _disposed;

        public Memory<byte> Memory => new(_buffer, 0, _length);

        public void Dispose()
        {
            if (_disposed)
                return;

            ArrayPool<byte>.Shared.Return(_buffer, clearArray: false);
            _disposed = true;
        }
    }
}
