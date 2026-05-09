namespace PerfCapture;

public enum PerfCaptureRequirement
{
    PerfInstalled,
    IntelPtAvailable,
    HardwareTraceAddressFilteringAvailable,
    PerfRecordControlAvailable,
    ElevatedPrivilegesLikelyRequired,
    KernelCoreAccessLikelyRequired,
    DotNetPerfMapEnabled
}
