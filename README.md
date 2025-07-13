# Custom Parquet Trace Format

The provided sample trace (`parquet_output` directory) is organised as
follows:

```
parquet_output/
  <pid>/                # process identifier
    <tid>/              # thread identifier
      segmentN.parquet              # event data
      segmentN_stackranges.parquet  # stack range information
```

Each `segment*.parquet` file shares the same schema:

- `id` (uint64)
- `perfId` (uint64)
- `pid` (uint32)
- `tid` (uint32)
- `time` (uint64) – timestamp in nanoseconds
- `cpu` (uint32)
- `flags` (uint32)
- `ip` (uint64) – sample instruction pointer
- `addr` (uint64)
- `period` (uint64)
- `insnCnt` (uint64)
- `cycCnt` (uint64)
- `weight` (uint64)
- `cpumode` (uint8)
- `addrCorrelatesSym` (uint8)
- `event` (string, optional)
- `machinePid` (uint32)
- `vcpu` (uint32)
- `ipSymoff` (uint32)
- `ipSym` (string, optional)
- `ipSymStart` (uint64)
- `ipSymEnd` (uint64)
- `ipDso` (string, optional)
- `ipSymBinding` (uint8)
- `ipIs64Bit` (uint8)
- `ipIsKernelIp` (uint8)
- `ipBuildId` (binary, optional)
- `ipFiltered` (uint8)
- `ipComm` (string, optional)
- `haveAddress` (bool)
- `addressSymoff` (uint32)
- `addressSym` (string, optional)
- `addressSymStart` (uint64)
- `addressSymEnd` (uint64)
- `addressDso` (string, optional)
- `addressSymBinding` (uint8)
- `addressIs64Bit` (uint8)
- `addressIsKernelIp` (uint8)
- `addressBuildId` (binary, optional)
- `addressFiltered` (uint8)
- `addressComm` (string, optional)

The companion `segment*_stackranges.parquet` files simply contain two
int64 columns:

- `startTrace`
- `endTrace`

These mark ranges of events associated with a particular stack.