namespace PerfConverter;public interface ITraceProcessor
{
    unsafe void FilterEventEarly(PerfDlFilterSample* sample);
}