# Testing Documentation

This document describes the testing strategy and implementation for PerfConverter.

## Overview

The PerfConverter project now includes comprehensive testing infrastructure that covers:

- **Unit Tests**: Testing individual components and data structures
- **Integration Tests**: Testing component interactions and data flow
- **CLI Tests**: Testing command-line interface functionality
- **Continuous Integration**: Automated testing via GitHub Actions

## Test Projects

### 1. Temp.Core.Tests
Tests the core batching functionality in `Temp.Core`:
- `BatcherTests`: Tests the asynchronous batching system for efficient data processing

### 2. PerfConverter.Tests
Tests the data models and structures in `Temp.Schema`:
- `TraceEntryTests`: Tests the main data structure for perf trace entries

### 3. CLI.Tests
Tests the command-line interface:
- `CommandLineTests`: Tests argument parsing, options handling, and validation

### 4. Integration.Tests
Tests end-to-end scenarios and component interactions:
- `ParquetIntegrationTests`: Tests data creation, processing, and edge cases

## Running Tests

### Prerequisites
- .NET 8.0 SDK or later
- Linux environment (recommended for full compatibility)

### Run All Tests
```bash
# Run individual test projects
dotnet test tests/Temp.Core.Tests/Temp.Core.Tests.csproj
dotnet test tests/PerfConverter.Tests/PerfConverter.Tests.csproj
dotnet test tests/CLI.Tests/CLI.Tests.csproj
dotnet test tests/Integration.Tests/Integration.Tests.csproj
```

### Run with Coverage
```bash
dotnet test tests/Temp.Core.Tests/Temp.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

## GitHub Actions CI/CD

The project includes automated testing via GitHub Actions:

- **Workflow**: `.github/workflows/test.yml`
- **Triggers**: Push to master, Pull Requests
- **Environment**: Ubuntu Latest with .NET 8.0
- **Coverage**: All test projects with verbose output

### CI Pipeline Steps
1. Checkout code
2. Setup .NET 8.0 SDK
3. Restore dependencies
4. Build test projects
5. Run all test suites
6. Install perf tools (for documentation)
7. Generate test summary

## Testing Strategy

### Mock Data Approach
Since intel_pt requires bare metal Intel CPUs, the tests use:
- **Synthetic Data**: Generated `TraceEntry` objects with realistic values
- **Mock Implementations**: Mock batch persistence for testing data flow
- **Edge Case Testing**: Boundary conditions, null values, and error scenarios

### Integration Testing
The integration tests focus on:
- Data structure validation
- Component interaction without requiring actual perf data
- Batch processing logic
- Error handling and edge cases

### Future Enhancements
1. **Real Data Tests**: Tests with actual small perf data files (when available)
2. **Performance Tests**: Benchmarking batch processing performance
3. **End-to-End Tests**: Full CLI workflow with mock perf execution
4. **Parquet Validation**: Reading and validating generated Parquet files

## Test Data

### Sample TraceEntry
The tests use factory methods to create consistent test data:
```csharp
var entry = CreateTestTraceEntry(id: 1);
// Creates entry with realistic CPU addresses, symbols, etc.
```

### Mock Batch Persistence
Tests use a mock implementation that:
- Tracks persisted items
- Counts batch operations
- Simulates async disposal

## Coverage

Current test coverage includes:
- ✅ Data structures and serialization
- ✅ Batching and async processing
- ✅ Command-line argument parsing
- ✅ Mock data generation and validation
- ⏳ Parquet file generation (requires full infrastructure)
- ⏳ Native C/C# interop testing (requires perf tools)

## Notes for Contributors

1. **Test Framework**: Uses xUnit with FluentAssertions for readable assertions
2. **Async Testing**: Properly handles async/await patterns in batch processing
3. **Resource Cleanup**: Tests clean up temporary resources (files, directories)
4. **Deterministic**: Tests use predictable data for consistent results
5. **Fast Execution**: Unit tests run quickly for rapid feedback

## Limitations

- **Intel PT**: Full integration testing requires bare metal Intel CPU
- **Perf Tools**: Some integration scenarios require Linux perf utilities
- **.NET Versions**: Test projects use .NET 8.0 for CI compatibility
- **Mock Data**: Some tests use synthetic data instead of real perf traces

This testing infrastructure provides a solid foundation for ensuring code quality while working within the constraints of the specialized hardware and tooling requirements.