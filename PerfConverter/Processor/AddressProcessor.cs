using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter.Processor;

public unsafe class AddressProcessor(IStringProcessor symProcessor, IStringProcessor commProcessor,  IPersister<AddressEntry> persistence) : IAddressProcessor
{
    ulong _currenAddress = 0;

    public unsafe void ProcessAddress(PerfDlfilterFns* fns, ulong traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_addr(ctx);
        if (resolved != null)
        {
            Process(resolved, traceId, pid, isIp: false);
        }
    }

    public unsafe void ProcessIp(PerfDlfilterFns* fns, ulong traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_ip(ctx);
        if (resolved == null) return;

        Process(resolved, traceId, pid, isIp: true);
    }

    unsafe void Process(PerfDlfilterAl* info, ulong traceId, int pid, bool isIp)
    {
        ulong symStrId = 0;
        if (info->sym != 0)
        {
            var str = Marshal.PtrToStringUTF8(info->sym)!;
            symStrId = symProcessor.Process(str);
        }
        if(info->comm != 0)
        {
            var comm = Marshal.PtrToStringUTF8(info->comm)!;
            commProcessor.Process(comm);
        }

        var buildId = new Span<byte>(info->buildid, info->buildid_size).ToArray();

        persistence.Persist(new AddressEntry
        {
            Id = _currenAddress++,
            TraceId = traceId,
            Address = info->addr,
            Pid = (uint)pid,
            IsIp = isIp,
            Size = info->size,
            Symoff = info->symoff,
            SymStrId = symStrId,
            SymStart = info->sym_start,
            SymEnd = info->sym_end,
            Dso = (ulong)info->dso,
            SymBinding = info->sym_binding,
            Is64Bit = info->is_64_bit,
            IsKernelIp = info->is_kernel_ip,
            BuildId = buildId,
            Filtered = info->filtered,
            Comm = (ulong)info->comm,
            Priv = (ulong)info->priv
        });
    }
}
