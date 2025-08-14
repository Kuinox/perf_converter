# PerfConverter Copilot Instructions

**Always follow these instructions first and fallback to additional search and context gathering only if the information in these instructions is incomplete or found to be in error.**

PerfConverter is a .NET-based tool for converting Linux perf data into Parquet format for analysis. It consists of multiple components that work together to process Linux perf event data through a DLFilter interface.

## Working Effectively

### Prerequisites and Setup
Install .NET 10 preview SDK - this is **REQUIRED**:
```bash
curl -sSL https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir /tmp/dotnet10
export PATH="/tmp/dotnet10:$PATH"
```

Verify installation:
```bash
dotnet --version  # Should show 10.0.x preview
which perf        # Linux perf tool must be available
```

### Building the Solution
Build the entire solution - this takes only seconds:
```bash
dotnet build
```

This single command handles all projects and their dependencies automatically.

### Publishing (Release Builds)
For native library publishing:

```bash
# Publish PerfConverter as native library
dotnet publish PerfConverter/PerfConverter.csproj -c Release

# Publish CLI tool (includes PerfConverter.so)
dotnet publish CLI/CLI.csproj -c Release
```

### Running the Tools
**Important**: The CLI tool requires specific machine configurations and cannot be run on most development machines. It is designed to work with Linux perf data collection infrastructure.

For development and testing purposes:
```bash
# Show help (may work on development machines)
dotnet run --project CLI/CLI.csproj -- --help

# Other operations require specific perf configuration not available in typical development environments
```

For published builds:
```bash
/tmp/dotnet10/dotnet CLI/bin/Release/net9.0/publish/CLI.dll --help
```

## Validation

### Always Test Build Functionality
After making code changes, always validate:

1. **Build the solution** - verify no compilation errors:
```bash
dotnet build
```

2. **Test CLI help functionality** - verify it starts and shows help (if environment supports it):
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
After making changes:

1. Build the solution
2. Test CLI help functionality (if environment supports it)  
3. For PerfConverter changes, verify the .so library is a valid ELF shared object

Note: Full functional testing requires specific Linux perf configuration not typically available in development environments.

### No Automated Tests
This project has **no automated test suite**. All validation must be done manually by building and running the applications.

## Common Tasks

### Project Structure Overview
```
PerfConverter/           # Core AOT library (compiles to .so for perf DLFilter)
CLI/                    # Command-line tool (orchestrates perf execution)  
Temp.Schema/           # Data persistence layer (Parquet format)
HelloWorld/           # Demo project (targets .NET 10.0 preview)
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
- Solution build: seconds
- Release publishing: typically under a minute
- AOT compilation is included but optimized for speed in the build process

### Troubleshooting
- **Build fails with .NET SDK error**: Ensure .NET 10 preview SDK is installed and in PATH
- **CLI execution fails**: The CLI tool requires specific Linux perf configuration not available in typical development environments
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