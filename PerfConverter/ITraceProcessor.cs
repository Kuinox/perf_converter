namespace PerfConverter;

public interface ITraceProcessor
{
    unsafe long FilterEventEarly(PerfDlFilterSample* sample);
    void Flush();
}