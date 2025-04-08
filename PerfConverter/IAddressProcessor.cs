using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfConverter;

internal interface IAddressProcessor
{
    unsafe void ProcessAddress(PerfDlfilterFns* fns, long traceId, int pid, void* ctx);
    unsafe void ProcessIp(PerfDlfilterFns* fns, long traceId, int pid, void* ctx);
}
