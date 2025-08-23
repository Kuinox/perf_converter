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

## Protocol Buffer Source

This project uses the official Perfetto protocol buffer definitions via a git submodule. The proto files are sourced directly from the [Google Perfetto repository](https://github.com/google/perfetto) located in the `perfetto-submodule` directory.

## Git Submodule with Sparse Checkout

To avoid cloning the entire 141MB Perfetto repository when we only need the proto files (3.7MB), this project uses **sparse checkout** to only include the `protos/` directory.

### Advantages

- **Official source**: Always uses the canonical Perfetto proto definitions
- **Minimal size**: Only 3.7MB instead of 141MB (97% size reduction)
- **Easy updates**: Use `git submodule update --remote` to get the latest proto definitions  
- **No file copying**: Avoids copying 405+ proto files into our repository
- **Better maintainability**: Always synchronized with the official Perfetto project

### Setting up the submodule

When cloning this repository, use the provided setup script to automatically configure sparse checkout:

```bash
# Run the setup script to configure sparse checkout
./Perfetto.Protos/setup-submodule.sh
```

Alternatively, manually initialize with sparse checkout:

```bash
# Initialize submodule
git submodule init Perfetto.Protos/perfetto-submodule
git submodule update Perfetto.Protos/perfetto-submodule

# Configure sparse checkout to only include protos/
cd Perfetto.Protos/perfetto-submodule
git config core.sparseCheckout true
echo "protos/*" > ../../.git/modules/Perfetto.Protos/perfetto-submodule/info/sparse-checkout
git read-tree -m -u HEAD
```

### Updating proto definitions

To update to the latest Perfetto proto definitions:

```bash
cd Perfetto.Protos/perfetto-submodule
git submodule update --remote
# Sparse checkout configuration will be preserved
```

## Extending This Project

The complete Perfetto protocol buffer definition includes hundreds of message types. Due to complex interdependencies between proto files, this project currently includes a working subset.

To add more proto definitions:

1. Add the proto file path to the `<Protobuf Include="perfetto-submodule/protos/...">` items in the `.csproj` file
2. Ensure all import dependencies are also included
3. Build and resolve any missing dependencies
4. Update the submodule if you need newer proto definitions: `git submodule update --remote`

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