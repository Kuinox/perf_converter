# PerfConverter.Fuchsia Tests

This project contains tests to validate the Fuchsia serialization functionality using the Perfetto trace processor.

## Setup

1. Download the Perfetto trace processor for your platform from:
   - https://perfetto.dev/docs/quickstart/binary-reference

2. Set the environment variable `TRACE_PROCESSOR_PATH` to the path of the trace_processor_shell executable:

   ### On Windows
   ```
   set TRACE_PROCESSOR_PATH=C:\path\to\trace_processor_shell.exe
   ```

   ### On Linux/macOS
   ```
   export TRACE_PROCESSOR_PATH=/path/to/trace_processor_shell
   ```

## Running Tests

After setting up the environment variable, run the tests using:

```
dotnet test PerfConverter.Fuchsia.Tests
```

## Test Overview

The tests verify that the Fuchsia serialization format is properly implemented and can be read by the Perfetto trace processor. Each test:

1. Creates a trace file using the Fuchsia serialization classes
2. Writes the trace to a temporary file
3. Uses the Perfetto trace processor to validate that the file is readable

### Test Cases

- **SimpleEvent_ShouldProduceValidTrace**: Tests basic event serialization
- **EventWithArguments_ShouldProduceValidTrace**: Tests events with arguments
- **CompleteEvent_ShouldProduceValidTrace**: Tests a complete duration event with start/end timestamps