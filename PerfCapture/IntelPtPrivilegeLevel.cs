namespace PerfCapture;

[Flags]
public enum IntelPtPrivilegeLevel
{
    None = 0,
    User = 1 << 0,
    Kernel = 1 << 1,
    UserAndKernel = User | Kernel
}
