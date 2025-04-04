using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfConverter;

internal interface IAddressProcessor
{
    unsafe void HandleAdress(PerfDlfilterFns* fns, int pid, void* addr);
    unsafe void ResolveIp(PerfDlfilterFns* fns, int pid, void* ip);
}
