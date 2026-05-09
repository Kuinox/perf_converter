# PerfConverter E2E Tests

These tests are opt-in because they require SSH access to a Linux host with `perf`, Intel PT, GCC, and the .NET SDK.

Run from the repository root:

```powershell
$env:PERFCONVERTER_E2E_REMOTE = "<user>@<host>"
$env:PERFCONVERTER_E2E_KEY = "<path-to-ssh-key>"
$env:PERFCONVERTER_E2E_REMOTE_REPO = "<remote-repo-path>"
$env:PERFCONVERTER_E2E_ENABLE_REMOTE = "1"
dotnet test PerfConverter.E2E.Tests\PerfConverter.E2E.Tests.csproj --filter TestCategory=E2E
```

The test expects `PERFCONVERTER_E2E_REMOTE_REPO` to point at an existing checkout on the remote host. It copies only `Targets/e2e_stack_target.c` into a remote temp directory, builds the tools from that checkout, records the target with Intel PT, converts perf data to parquet with the CLI, runs `StackFixer`, emits a Perfetto trace, then validates the reconstructed stack parquet locally.
