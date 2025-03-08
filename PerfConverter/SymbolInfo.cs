using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public struct SymbolInfo
{
    public IntPtr sym;
    public IntPtr module;
}
