using PerfConverter.PerfStructs;

namespace PerfConverter.Processor;

public interface ITraceProcessor
{
    unsafe ulong QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address);
}