# Perfetto Protocol Buffer Definitions for C#

This project contains C# definitions generated from Perfetto protocol buffer files.

## What's Included

This project currently includes a working subset of the Perfetto protocol buffers:

### Common Protos
- **SystemInfo**: System information and capabilities
- **TraceStats**: Statistics about trace collection
- **SysStatsCounters**: System performance counters
- **AndroidLogConstants**: Android logging constants
- **PerfEvents**: Performance event definitions
- **ProtologCommon**: Protocol buffer logging common types

### Trace Protos
- **TraceUuid**: Unique identifiers for traces
- **Trigger**: Trace triggering mechanisms
- **TestEvent**: Test events for validation
- **UiState**: UI state tracking

### Track Event Protos
- **DebugAnnotation**: Debug annotations for events
- **SourceLocation**: Source code location information
- **TaskExecution**: Task execution tracking
- **LogMessage**: Log message structures

### Process and System Tracking
- **ProcessTree**: Process hierarchy information
- **ProcessStats**: Process statistics
- **SysStats**: System-wide statistics
- **InternedData**: Interned string and data storage

## Usage

```csharp
using Perfetto.Protos;

// Example: Create a system info object
var systemInfo = new SystemInfo
{
    Utsname = new Utsname
    {
        Sysname = "Linux",
        Version = "5.4.0",
        Release = "Ubuntu",
        Machine = "x86_64"
    }
};
```

## Extending This Project

The complete Perfetto protocol buffer definition includes hundreds of message types. Due to complex interdependencies between proto files, this project currently includes a working subset.

To add more proto definitions:

1. Add the proto file path to the `<Protobuf Include="...">` items in the `.csproj` file
2. Ensure all import dependencies are also included
3. Build and resolve any missing dependencies

### Known Limitations

- The main `trace_packet.proto` is not included due to its extensive dependencies
- Some proto files have circular or complex import chains that require careful ordering
- Service-oriented protos (like `bigtrace`, `trace_processor`) are excluded

## Building

```bash
dotnet build
```

The build process automatically generates C# classes from the `.proto` files using the Google Protocol Buffer compiler.

## Dependencies

- **Google.Protobuf**: Core protobuf runtime for C#
- **Grpc.Tools**: Build-time tools for code generation

## Generated Code

Generated C# classes are placed in the `Perfetto.Protos` namespace and follow standard protobuf naming conventions.