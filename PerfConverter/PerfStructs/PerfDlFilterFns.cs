namespace PerfConverter.PerfStructs;

using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlfilterFns
{
    public delegate* unmanaged<void*, PerfDlfilterAl*> resolve_ip;
    public delegate* unmanaged<void*, PerfDlfilterAl*> resolve_addr;
    public delegate* unmanaged<void*, int*, nint> args;
    public delegate* unmanaged<void*, ulong, PerfDlfilterAl*, int> resolve_address;
    public delegate* unmanaged<void*, uint*, byte*> insn;
    public delegate* unmanaged<void*, uint*, nint> srcline;
    public delegate* unmanaged<void*, nint> attr;
    public delegate* unmanaged<void*, ulong, void*, uint, int> object_code;
    public delegate* unmanaged<void*, PerfDlfilterAl*, void> al_cleanup;
    fixed long reserved[119];
}
