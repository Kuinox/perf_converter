using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfConverter;

internal interface IAddressProcessor
{
    unsafe void ProcessAddress(PerfDlfilterFns* fns, int pid, void* addr);
    unsafe void ProcessIp(PerfDlfilterFns* fns, int pid, void* ip);
}
