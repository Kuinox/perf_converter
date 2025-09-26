using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace PerfConverter.Entry;

[StructLayout(LayoutKind.Sequential)]
public struct TraceEntry
{
    public ulong Id;
    public ulong PerfId;
    public ushort InstructionLatency;
    public uint Pid;
    public uint Tid;
    public ulong Time;
    public uint Cpu;
    public DLFilterFlag Flags;
    public ulong Period;
    public ulong InsnCnt;
    public ulong CycCnt;
    public ulong Weight;
    public byte Cpumode;
    public byte AddrCorrelatesSym;
    public string Event;
    public uint MachinePid;
    public uint Vcpu;

    // ip
    public ulong IpAddress;
    public uint IpSymoff;
    public string? IpSym;
    public ulong IpSymStart;
    public ulong IpSymEnd;
    public string? IpDso;
    public byte IpSymBinding;
    public byte IpIs64Bit;
    public byte IpIsKernelIp;
    public byte[] IpBuildId;
    public byte IpFiltered;
    public string? IpComm;

    // address
    public bool HaveAddress;
    public ulong AddressAddress;
    public uint AddressSymoff;
    public string? AddressSym;
    public ulong AddressSymStart;
    public ulong AddressSymEnd;
    public string? AddressDso;
    public byte AddressSymBinding;
    public byte AddressIs64Bit;
    public byte AddressIsKernelIp;
    public byte[] AddressBuildId;
    public byte AddressFiltered;
    public string? AddressComm;

    // Static pools for reusing string and byte array allocations
    private static readonly ConcurrentDictionary<string, WeakReference<string>> StringPool = new();
    private static readonly ConcurrentDictionary<int, WeakReference<byte[]>> ByteArrayPool = new();

    // UTF-8 byte content cache for avoiding Marshal.PtrToStringUTF8 allocations
    private static readonly ConcurrentDictionary<int, (WeakReference<string> StringRef, byte[] ByteContent)> Utf8ByteCache = new();

    public override readonly string ToString() => IpDso ?? string.Format("0x{0:X}", IpAddress);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GetOrCreateByteArray(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return [];

        var hash = GetByteArrayHash(source);
        if (ByteArrayPool.TryGetValue(hash, out var weakRef) &&
            weakRef.TryGetTarget(out var cachedArray) &&
            cachedArray.AsSpan().SequenceEqual(source))
        {
            return cachedArray;
        }

        var newArray = source.ToArray();
        ByteArrayPool.TryAdd(hash, new WeakReference<byte[]>(newArray));
        return newArray;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetByteArrayHash(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        foreach (var b in bytes)
        {
            hash.Add(b);
        }
        return hash.ToHashCode();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe string GetOrInternStringFromPtr(nint ptr)
    {
        if (ptr == 0) return string.Empty;

        // Read UTF-8 bytes directly from pointer until null terminator
        var bytePtr = (byte*)ptr;
        var length = 0;
        while (bytePtr[length] != 0) length++;

        if (length == 0) return string.Empty;

        var bytes = new ReadOnlySpan<byte>(bytePtr, length);

        // Hash the byte content using HashCode API
        var hash = new HashCode();
        hash.AddBytes(bytes);
        var hashValue = hash.ToHashCode();

        // Check cache for existing string with same byte content
        if (Utf8ByteCache.TryGetValue(hashValue, out var cached) &&
            cached.StringRef.TryGetTarget(out var cachedString) &&
            cached.ByteContent.AsSpan().SequenceEqual(bytes))
        {
            return cachedString;
        }

        // Cache miss - use Marshal to create string and cache it
        var newString = Marshal.PtrToStringUTF8(ptr)!;
        var internedString = string.Intern(newString);
        var bytesCopy = bytes.ToArray();

        Utf8ByteCache.TryAdd(hashValue, (new WeakReference<string>(internedString), bytesCopy));
        return internedString;
    }

    public static unsafe TraceEntry CreateFromPerf(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address, ulong id)
    {
        var entry = new TraceEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = (DLFilterFlag)sample->flags,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = GetOrInternStringFromPtr((nint)sample->@event),
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu,

            IpAddress = ip->addr,
            IpSymoff = ip->symoff,
            IpSym = GetOrInternStringFromPtr((nint)ip->sym),
            IpSymStart = ip->sym_start,
            IpSymEnd = ip->sym_end,
            IpDso = GetOrInternStringFromPtr((nint)ip->dso),
            IpSymBinding = ip->sym_binding,
            IpIs64Bit = ip->is_64_bit,
            IpIsKernelIp = ip->is_kernel_ip,
            IpBuildId = GetOrCreateByteArray(new Span<byte>(ip->buildid, ip->buildid_size)),
            IpFiltered = ip->filtered,
            IpComm = GetOrInternStringFromPtr((nint)ip->comm)
        };

        if (address != null)
        {
            entry.HaveAddress = true;
            entry.AddressAddress = address->addr;
            entry.AddressSymoff = address->symoff;
            entry.AddressSym = GetOrInternStringFromPtr((nint)address->sym);
            entry.AddressSymStart = address->sym_start;
            entry.AddressSymEnd = address->sym_end;
            entry.AddressDso = GetOrInternStringFromPtr((nint)address->dso);
            entry.AddressSymBinding = address->sym_binding;
            entry.AddressIs64Bit = address->is_64_bit;
            entry.AddressIsKernelIp = address->is_kernel_ip;
            entry.AddressBuildId = GetOrCreateByteArray(new Span<byte>(address->buildid, address->buildid_size));
            entry.AddressFiltered = address->filtered;
            entry.AddressComm = GetOrInternStringFromPtr((nint)address->comm);
        }

        return entry;
    }

    public static void CleanupPools()
    {
        // Clean up dead weak references
        var deadStringKeys = new List<string>();
        foreach (var kvp in StringPool)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                deadStringKeys.Add(kvp.Key);
            }
        }
        foreach (var key in deadStringKeys)
        {
            StringPool.TryRemove(key, out _);
        }

        var deadByteKeys = new List<int>();
        foreach (var kvp in ByteArrayPool)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                deadByteKeys.Add(kvp.Key);
            }
        }
        foreach (var key in deadByteKeys)
        {
            ByteArrayPool.TryRemove(key, out _);
        }

        var deadUtf8Keys = new List<int>();
        foreach (var kvp in Utf8ByteCache)
        {
            if (!kvp.Value.StringRef.TryGetTarget(out _))
            {
                deadUtf8Keys.Add(kvp.Key);
            }
        }
        foreach (var key in deadUtf8Keys)
        {
            Utf8ByteCache.TryRemove(key, out _);
        }
    }
}
