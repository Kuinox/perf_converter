# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PerfConverter is a .NET-based tool for converting Linux perf data into Parquet format for analysis. The project consists of several components:

1. **PerfConverter**: Core library that processes Linux perf event data through a DLFilter, extracting trace samples, addresses, symbols, and other information. Compiled as a shared library (.so) for use with the Linux perf tool.

2. **CLI**: Command-line wrapper tool that simplifies running perf with the PerfConverter DLFilter. Handles auxiliary data extraction and orchestrates the perf script execution.

3. **Temp.Schema**: Schema definitions and persistence layer for storing processed data in Parquet format.

4. **PostProcess**: Utility for post-processing the converted data (appears to be in early development).

5. **native**: Contains C code that interfaces with Linux perf via exported functions.

6. **HelloWorld**: Simple test/demo project.

## Build Commands

### Build the entire solution

```bash
dotnet build PerfConverter.sln
```

### Build individual projects

```bash
# Build the core library
dotnet build PerfConverter/PerfConverter.csproj

# Build the CLI tool
dotnet build CLI/CLI.csproj

# Build the schema library
dotnet build Temp.Schema/Temp.Schema.csproj

# Build the post processor
dotnet build PostProcess/PostProcess.csproj
```

### Publish with AOT (Ahead-of-Time) compilation

The PerfConverter project is configured for AOT compilation and targets Linux x64. The CLI project automatically handles building and copying the PerfConverter.so file:

```bash
# Publish the core library as a shared library
dotnet publish PerfConverter/PerfConverter.csproj -c Release

# Publish the CLI tool (includes PerfConverter.so)
dotnet publish CLI/CLI.csproj -c Release
```

## Running the Tools

### CLI Tool (Recommended)

The CLI tool provides a convenient wrapper for running perf with the PerfConverter DLFilter:

```bash
dotnet run --project CLI/CLI.csproj -- <input-file> [options]
```

Options:
- `--perf-args, -p`: Additional arguments to pass to perf script (default: "-f --itrace=bei0ns")
- `--output, -o`: Output directory for Parquet files (default: "parquet_output")
- `--dry-run, -n`: Show the command that would be executed without running it

Example:
```bash
dotnet run --project CLI/CLI.csproj -- /path/to/perf.data --output ./output --perf-args "-f --itrace=bei0ns"
```

### Direct perf Usage

You can also run perf directly with the PerfConverter DLFilter:

```bash
perf script -f --itrace=bei0ns -i /path/to/perf.data --dlfilter /path/to/PerfConverter.so
```

### Environment Variables

The following environment variables control PerfConverter's behavior:

- `MAX_TRACES_TO_PROCESS`: Maximum number of traces to process before exiting
- `PERSISTENCE_TYPE`: The type of persistence to use (currently only Parquet is supported)
- `OUTPUT_DIRECTORY`: Directory where output files will be written (default: "parquet_output")
- `PARQUET_COMPRESSION`: Compression method for Parquet output (None, Gzip, Snappy) - default is Snappy
- `BATCH_SIZE`: Number of items to batch before writing to disk (default: 10,000,000)
- `ENABLE_PROGRESS_SIGNALS`: When set to `true`, enables verbose progress and file activity signals (PROGRESS, FILE_STATUS, FILE_ACTIVITY). Default is `false` to avoid console spam when running manually

## Architecture

### Data Flow

1. **CLI Tool**: The CLI project orchestrates the entire workflow by:
   - Extracting auxiliary data loss events from perf data files using `AuxDataExtractor`
   - Setting up environment variables for output directory and aux data
   - Executing the perf script command with the PerfConverter DLFilter
   
2. **DLFilter Processing**: Linux perf data is processed through the native DLFilter interface:
   - The native code in `exports.c` defines structures and functions for the perf DL filter
   - The C# code in `PerfConverter.PerfStructs` defines corresponding structures
   - The `PerfDlFilter` class (Program.cs) serves as the main entry point with `[UnmanagedCallersOnly]` exports

3. **Data Processing**: The `TraceProcessor` handles different types of perf events and distributes them to appropriate processors

4. **Persistence**: The `Batcher` class from `Temp.Schema` buffers data in memory before writing to disk via Parquet persistence implementations

### Native Code Integration

The project uses a combination of C# and native C code to interface with Linux perf:

1. The native code in `exports.c` defines structures and functions for the perf DL filter
2. The C# code in `PerfConverter.PerfStructs` defines corresponding structures (`PerfDlfilterFns`, `PerfDlFilterSample`, etc.)
3. The `[UnmanagedCallersOnly]` attribute is used to export functions that can be called from native code
4. The PerfConverter project compiles to a shared library (.so) that acts as a perf DLFilter plugin

### Key Components

- **PerfDlFilter** (PerfConverter/Program.cs): Main entry point for processing perf events, handles initialization and event processing
- **TraceProcessor** (PerfConverter/TraceProcessor.cs): Processes trace samples and coordinates with persistence layer
- **CLI** (CLI/Program.cs): Command-line interface that orchestrates perf execution and handles auxiliary data extraction
- **AuxDataExtractor** (CLI/AuxDataExtractor.cs): Extracts auxiliary data loss events from perf data files
- **Persistence Layer** (Temp.Schema/Persistence/): Handles batching and writing data to Parquet files
- **Schema Definitions** (Temp.Schema/): Defines data structures for trace entries, stack ranges, and Parquet schemas

## Development Environment

### Platform Requirements

- **Target Platform**: Linux x64 (WSL2 is supported for development)
- **.NET Version**: .NET 9.0
- **Native Dependencies**: Linux perf tool must be available in PATH
- **AOT Compilation**: Projects are configured for Ahead-of-Time compilation for performance

### Project Dependencies

The solution uses several key dependencies:
- **ParquetDotNet**: For Parquet file format support
- **System.CommandLine**: For CLI argument parsing
- **Microsoft.LinuxTracepoints.Decode**: For Linux tracepoint decoding
- **System.Text.Json**: For JSON serialization (auxiliary data handling)

### Build System Notes

- The CLI project has custom MSBuild targets that automatically build and copy the PerfConverter.so file
- AOT compilation is configured in the PerfConverter project to produce a native shared library
- The build process is Linux-specific due to the native perf integration