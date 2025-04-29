using System.Runtime.InteropServices;
using System.Threading.Channels;
using PerfConverter.Persistance;

namespace PerfConverter;

public unsafe class AddressProcessor : IAddressProcessor
{
    private ulong _currenAddress = 0;
    private readonly SymProcessor _sqlSymProcessor;
    private readonly Channel<AddressEntry> _channel;
    private readonly Task _workThread;

    private AddressProcessor(SymProcessor sqlSymProcessor, IAddressPersistance persistance)
    {
        var batchSize = 1_000_000;
        _sqlSymProcessor = sqlSymProcessor;
        _channel = Channel.CreateBounded<AddressEntry>(batchSize);
        _workThread = BackgroundBatching<AddressEntry>.Run(batchSize, _channel.Reader, persistance.Persist);
    }

    public unsafe void ProcessAddress(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_addr(ctx);
        if (resolved != null)
        {
            Process(resolved, traceId, pid, isIp: false);
        }
    }

    public unsafe void ProcessIp(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_ip(ctx);
        if (resolved == null) return;

        Process(resolved, traceId, pid, isIp: true);
    }

    private unsafe void Process(PerfDlfilterAl* info, long traceId, int pid, bool isIp)
    {
        long symStrId = 0;
        if (info->sym != 0)
        {
            var str = Marshal.PtrToStringUTF8(info->sym)!;
            symStrId = _sqlSymProcessor.Process(str);
        }

        var buildId = new Span<byte>(info->buildid, info->buildid_size).ToArray();

        _channel.Writer.Write(new AddressEntry
        {
            Id = _currenAddress++,
            TraceId = traceId,
            Address = info->addr,
            Pid = pid,
            IsIp = isIp,
            Size = (int)info->size,
            Symoff = (int)info->symoff,
            SymStrId = symStrId,
            SymStart = info->sym_start,
            SymEnd = info->sym_end,
            Dso = info->dso,
            SymBinding = info->sym_binding,
            Is64Bit = info->is_64_bit,
            IsKernelIp = info->is_kernel_ip,
            BuildId = buildId,
            Filtered = info->filtered,
            Comm = info->comm,
            Priv = info->priv
        });
    }
    public void Close()
    {
        _channel.Writer.Complete();
        _workThread.Wait();
        _sqlSymProcessor.Flush();
    }
}
