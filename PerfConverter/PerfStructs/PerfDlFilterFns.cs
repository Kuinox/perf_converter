namespace PerfConverter;

using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlfilterFns
{
    public delegate* unmanaged<void*, PerfDlfilterAl*> resolve_ip;
    public delegate* unmanaged<void*, PerfDlfilterAl*> resolve_addr;
    public delegate* unmanaged<void*, int*, IntPtr> args;
    public delegate* unmanaged<void*, ulong, PerfDlfilterAl*, int> resolve_address;
    public delegate* unmanaged<void*, uint*, byte*> insn;
    public delegate* unmanaged<void*, uint*, IntPtr> srcline;
    public delegate* unmanaged<void*, IntPtr> attr;
    public delegate* unmanaged<void*, ulong, void*, uint, int> object_code;
    public delegate* unmanaged<void*, PerfDlfilterAl*, void> al_cleanup;
    fixed long reserved[119];
}
