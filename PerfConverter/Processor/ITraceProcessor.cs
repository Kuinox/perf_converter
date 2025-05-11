namespace PerfConverter.Processor;

public interface ITraceProcessor
{
    unsafe ulong FilterEventEarly(PerfDlFilterSample* sample);
}