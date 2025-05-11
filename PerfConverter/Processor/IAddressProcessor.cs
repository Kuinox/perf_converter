namespace PerfConverter.Processor;

interface IAddressProcessor
{
    unsafe void ProcessAddress(PerfDlfilterFns* fns, ulong traceId, int pid, void* ctx);
    unsafe void ProcessIp(PerfDlfilterFns* fns, ulong traceId, int pid, void* ctx);
}
