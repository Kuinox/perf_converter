# PerfConverter E2E Tests

These tests run locally on the Linux machine that has `perf`, Intel PT, GCC, and the .NET SDK. They skip automatically on non-Linux machines or when required perf tooling is unavailable.

Run from the repository root on that Linux machine:

```bash
dotnet test PerfConverter.E2E.Tests/PerfConverter.E2E.Tests.csproj --filter TestCategory=E2E
```

The test publishes the tools from the current checkout into a temp directory, compiles `Targets/e2e_stack_target.c`, records the target with Intel PT, converts perf data to parquet with the CLI, runs `StackFixer`, emits a Perfetto trace, then validates the reconstructed stack parquet.
