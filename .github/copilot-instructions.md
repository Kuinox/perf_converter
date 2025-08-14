# PerfConverter Copilot Instructions

**Always follow these instructions first and fallback to additional search and context gathering only if the information in these instructions is incomplete or found to be in error.**

PerfConverter is a .NET-based tool for converting Linux perf data into Parquet format for analysis. It consists of multiple components that work together to process Linux perf event data through a DLFilter interface.

## Working Effectively

### Prerequisites and Setup
Install .NET 9.0 SDK - this is **REQUIRED**:
```bash
curl -sSL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 9.0 --install-dir /tmp/dotnet9
export PATH="/tmp/dotnet9:$PATH"
```

Verify installation:
```bash
dotnet --version  # Should show 9.0.x
which perf        # Linux perf tool must be available
```

### Building the Solution
**NEVER CANCEL** build commands - they may take several minutes. Always set timeouts of 60+ minutes.

Build individual projects (recommended):
```bash
# Build schema library - takes ~35 seconds. NEVER CANCEL.
dotnet build Temp.Schema/Temp.Schema.csproj

# Build core PerfConverter library - takes ~15 seconds. NEVER CANCEL.
dotnet build PerfConverter/PerfConverter.csproj

# Build CLI tool (includes AOT compilation) - takes ~35 seconds. NEVER CANCEL.
dotnet build CLI/CLI.csproj
```

**WARNING**: Do NOT build the entire solution (`dotnet build PerfConverter.sln`) - the HelloWorld project targets .NET 10.0 which doesn't exist. Build individual projects instead.

### Publishing (Release Builds)
**NEVER CANCEL** - AOT compilation takes time. Set timeout to 60+ minutes.

```bash
# Publish PerfConverter as native library - takes ~20 seconds. NEVER CANCEL.
dotnet publish PerfConverter/PerfConverter.csproj -c Release

# Publish CLI tool (includes PerfConverter.so) - takes ~10 seconds. NEVER CANCEL.  
dotnet publish CLI/CLI.csproj -c Release
```

### Running the Tools
The CLI tool is the recommended entry point:

```bash
# Show help
dotnet run --project CLI/CLI.csproj -- --help

# Test with dry run (validates command without executing)
dotnet run --project CLI/CLI.csproj -- /path/to/perf.data --dry-run

# Process perf data
dotnet run --project CLI/CLI.csproj -- /path/to/perf.data --output ./output
```

For published builds, use the .NET runtime:
```bash
/tmp/dotnet9/dotnet CLI/bin/Release/net9.0/publish/CLI.dll --help
```

## Validation

### Always Test Build Functionality
After making code changes, always validate:

1. **Build all projects** - verify no compilation errors:
```bash
dotnet build Temp.Schema/Temp.Schema.csproj
dotnet build PerfConverter/PerfConverter.csproj  
dotnet build CLI/CLI.csproj
```

2. **Test CLI functionality** - verify it starts and shows help:
```bash
dotnet run --project CLI/CLI.csproj -- --help
```

3. **Verify native library generation** after PerfConverter changes:
```bash
dotnet publish PerfConverter/PerfConverter.csproj -c Release
ls -la PerfConverter/bin/Release/net9.0/linux-x64/publish/PerfConverter.so
file PerfConverter/bin/Release/net9.0/linux-x64/publish/PerfConverter.so
```

### Manual Scenario Testing
**ALWAYS** run a complete scenario test after making changes:

1. Build all components
2. Test CLI help functionality  
3. Test CLI with `--dry-run` option to verify command generation
4. For PerfConverter changes, verify the .so library is a valid ELF shared object

### No Automated Tests
This project has **no automated test suite**. All validation must be done manually by building and running the applications.

## Common Tasks

### Project Structure Overview
```
PerfConverter/           # Core AOT library (compiles to .so for perf DLFilter)
CLI/                    # Command-line tool (orchestrates perf execution)  
Temp.Schema/           # Data persistence layer (Parquet format)
HelloWorld/           # Demo project (DO NOT BUILD - targets .NET 10.0)
native/              # C code for perf DLFilter interface
viewer/              # Python-based data viewer (separate environment)
```

### Key Files and Locations
- **Main entry points**: `CLI/Program.cs`, `PerfConverter/Program.cs`
- **Native integration**: `native/exports.c`, `PerfConverter/PerfStructs.cs`
- **Persistence**: `Temp.Schema/Persistence/`, `PerfConverter/Persistence/`
- **Build configuration**: `*.csproj` files, `.editorconfig`
- **Documentation**: `README.md`, `CLAUDE.md`

### Environment Variables
The following control PerfConverter runtime behavior:
- `OUTPUT_DIRECTORY`: Output directory (default: "parquet_output")
- `MAX_TRACES_TO_PROCESS`: Maximum traces before exit
- `PARQUET_COMPRESSION`: Compression method (None, Gzip, Snappy)
- `BATCH_SIZE`: Items per batch (default: 10,000,000)

### Build Timing Expectations
- Temp.Schema build: ~35 seconds
- PerfConverter build: ~15 seconds  
- CLI build: ~35 seconds (includes AOT)
- PerfConverter Release publish: ~20 seconds
- CLI Release publish: ~10 seconds

**CRITICAL**: Always use timeouts of 60+ minutes for build commands. Build processes include AOT compilation and native code compilation which can be time-intensive.

### Troubleshooting
- **Build fails with .NET SDK error**: Ensure .NET 9.0 SDK is installed and in PATH
- **HelloWorld build fails**: This is expected - skip this project (targets .NET 10.0)
- **CLI execution fails**: Use the proper .NET 9.0 runtime, not the system's .NET 8.0
- **PerfConverter.so not found**: Ensure PerfConverter project was published, not just built

### Code Style
- Follow existing patterns in the codebase
- The project uses EditorConfig for formatting rules
- No linting tools are configured - follow .NET conventions
- AOT compilation requires careful handling of generics and reflection

### Architecture Notes
- **PerfConverter**: AOT-compiled native library that acts as a Linux perf DLFilter plugin
- **CLI**: Orchestrates perf execution, handles auxiliary data extraction, manages output
- **Native Integration**: C code exports functions that .NET code imports via UnmanagedCallersOnly
- **Persistence**: Batched writes to Parquet files for efficient storage and analysis