namespace PerfConverter.Processor;

internal interface IAddressProcessor
{
    unsafe void ProcessAddress(PerfDlfilterFns* fns, long traceId, int pid, void* ctx);
    unsafe void ProcessIp(PerfDlfilterFns* fns, long traceId, int pid, void* ctx);
}
