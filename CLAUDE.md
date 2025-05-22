# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PerfConverter is a .NET-based tool for converting Linux perf data into Parquet format for analysis. The project consists of several components:

1. **PerfConverter**: Core library that processes Linux perf event data through a DLFilter, extracting trace samples, addresses, symbols, and other information.

2. **PerfMetadataExtract**: Command-line utility to extract auxiliary data lost events from perf data files.

3. **Temp.Core**: Shared library containing batching functionality for efficient data processing.

4. **PostProcess**: Utility for post-processing the converted data (appears to be in early development).

5. **native**: Contains C code that interfaces with Linux perf via exported functions.

## Build Commands

### Build the entire solution

```bash
dotnet build PerfConverter.sln
```

### Build individual projects

```bash
# Build the core library
dotnet build PerfConverter/PerfConverter.csproj

# Build the metadata extractor
dotnet build PerfMetadataExtract/PerfMetadataExtract.csproj

# Build the post processor
dotnet build PostProcess/PostProcess.csproj
```

### Publish with AOT (Ahead-of-Time) compilation

PerfConverter is configured for AOT compilation and targets Linux x64:

```bash
dotnet publish PerfConverter/PerfConverter.csproj -c Release
```

## Running the Tools

### PerfMetadataExtract

```bash
dotnet run --project PerfMetadataExtract/PerfMetadataExtract.csproj -- <inputFile> <outputFilePath> [compression] [batchSize]
```

Parameters:
- `inputFile`: Path to the input perf data file
- `outputFilePath`: Path to the output file
- `compression` (optional): Compression method (None, Gzip, Snappy) - default is Snappy
- `batchSize` (optional): Batch size for processing - default is 2,000,000

### Environment Variables

The following environment variables control PerfConverter's behavior:

- `MAX_TRACES_TO_PROCESS`: Maximum number of traces to process before exiting
- `PERSISTENCE_TYPE`: The type of persistence to use (currently only Parquet is supported)
- `OUTPUT_DIRECTORY`: Directory where output files will be written (default: "parquet_output")
- `PARQUET_COMPRESSION`: Compression method for Parquet output (None, Gzip, Snappy) - default is Snappy
- `BATCH_SIZE`: Number of items to batch before writing to disk (default: 10,000,000)

## Architecture

### Data Flow

1. Linux perf data is processed through a native DLFilter (`exports.c`)
2. The C# code in `PerfConverter` interfaces with the native code via P/Invoke
3. Different processors handle different types of data:
   - `TraceProcessor`: Processes trace samples
   - `AddressProcessor`: Processes IP and address information
   - `StringProcessor`: Processes symbol and command names
4. The `Batcher` class from `Temp.Core` buffers data in memory before writing
5. Persistence implementations in `Persistence/ParquetDotNet` write data to Parquet files

### Native Code Integration

The project uses a combination of C# and native C code to interface with Linux perf:

1. The native code in `exports.c` defines structures and functions for the perf DL filter
2. The C# code in `PerfConverter.PerfStructs` defines corresponding structures
3. The `[UnmanagedCallersOnly]` attribute is used to export functions that can be called from native code

## Key Components

- **PerfDlFilter** (Program.cs): Main entry point for processing perf events
- **Processors**: Handle different types of data (trace samples, addresses, strings)
- **Persistence**: Implementations for storing processed data in Parquet format
- **Batching**: Efficient batch processing of data before persistence