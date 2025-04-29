namespace PerfConverter.Processor;

public interface ITraceProcessor
{
    unsafe long FilterEventEarly(PerfDlFilterSample* sample);
}