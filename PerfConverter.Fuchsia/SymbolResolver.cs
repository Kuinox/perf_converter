using System.Runtime.InteropServices;

namespace PerfConverter.Fuchsia;

/// <summary>
/// Symbol resolution and caching
/// </summary>
public unsafe class SymbolResolver
{
    public string? InternString(TraceState state, IntPtr intPtr)
    {
        if (intPtr == IntPtr.Zero) return null;
        
        if (state.SymbolCache.TryGetValue(intPtr, out var cachedSymbol))
        {
            return cachedSymbol;
        }
        
        var str = Marshal.PtrToStringUTF8(intPtr);
        state.SymbolCache[intPtr] = str;
        return str;
    }
    
    public string ResolveIp(PerfDlFilterSample* sample, void* ctx, byte[] buffer)
    {
        var dlfilter_fns = PerfDlFilter.get_perf_dlfilter_fns();
        var al = dlfilter_fns->resolve_ip(ctx);
        
        if (al != null && al->sym != IntPtr.Zero)
        {
            var symbol = Marshal.PtrToStringUTF8(al->sym);
            return symbol ?? FormatAddress(sample->ip);
        }
        
        return FormatAddress(sample->ip);
    }
    
    public string ResolveAddr(PerfDlFilterSample* sample, void* ctx, byte[] buffer)
    {
        if (sample->addr_correlates_sym != 0)
        {
            var dlfilter_fns = PerfDlFilter.get_perf_dlfilter_fns();
            var al = dlfilter_fns->resolve_addr(ctx);
            
            if (al != null && al->sym != IntPtr.Zero)
            {
                var symbol = Marshal.PtrToStringUTF8(al->sym);
                return symbol ?? FormatAddress((ulong)(long)sample->addr);
            }
        }
        
        return FormatAddress((ulong)(long)sample->addr);
    }
    
    private string FormatAddress(ulong address)
    {
        return $"0x{address:X}";
    }
}