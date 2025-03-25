using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using PerfConverter.Fuchsia;

namespace PerfConverter.Fuchsia.Tests;

[TestFixture]
public class FuchsiaSerializerTests
{
    private string _tempDirectory;
    private string _traceProcessorPath;
    private PerfettoTraceProcessor _traceProcessor;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Create a temporary directory for test files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"FuchsiaTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);

        // Set path to trace processor executable
        // This should be configured in a real environment
        _traceProcessorPath = Environment.GetEnvironmentVariable("TRACE_PROCESSOR_PATH") ?? 
            throw new InvalidOperationException("TRACE_PROCESSOR_PATH environment variable not set");

        _traceProcessor = new PerfettoTraceProcessor(_traceProcessorPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        //// Clean up temp directory
        //if (Directory.Exists(_tempDirectory))
        //    Directory.Delete(_tempDirectory, true);
    }

    [Test]
    public void SimpleEvent_ShouldProduceValidTrace()
    {
        // Arrange
        string traceFilePath = Path.Combine(_tempDirectory, "simple_event.ftf");
        
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Write magic number
            writer.Write(0x0016547846040010UL);
            
            // Provider info metadata
            var providerInfo = new MetadataRecord(
                MetadataType.ProviderInfo,
                0, // Provider ID
                "TestProvider");
            providerInfo.Write(writer);
            
            // Provider section metadata
            var providerSection = new MetadataRecord(
                MetadataType.ProviderSection,
                0); // Provider ID
            providerSection.Write(writer);
            
            // Thread record
            var threadRecord = new ThreadRecord(1, 1000, 1001);
            threadRecord.Write(writer);
            
            // String records
            var categoryString = new StringRecord(1, "TestCategory");
            categoryString.Write(writer);
            
            var nameString = new StringRecord(2, "TestEvent");
            nameString.Write(writer);
            
            // Event record (duration begin)
            var eventBegin = new EventRecord(
                EventType.DurationBegin,
                1000000, // timestamp
                1, // thread ref
                1, // category ref
                2); // name ref
            eventBegin.Write(writer);
            
            // Event record (duration end)
            var eventEnd = new EventRecord(
                EventType.DurationEnd,
                2000000, // timestamp
                1, // thread ref
                1, // category ref
                2); // name ref
            eventEnd.Write(writer);
            
            // Save to file
            File.WriteAllBytes(traceFilePath, ms.ToArray());
        }
        
        Console.WriteLine($"Trace file: {traceFilePath}");
        // Act
        bool isValid = _traceProcessor.ValidateTrace(traceFilePath);
        
        // Assert
        Assert.That(isValid, "Trace file should be valid");
    }

    [Test]
    public void EventWithArguments_ShouldProduceValidTrace()
    {
        // Arrange
        string traceFilePath = Path.Combine(_tempDirectory, "event_with_args.ftf");
        
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Write magic number
            writer.Write(0x0016547846040010UL);
            
            // Provider info metadata
            var providerInfo = new MetadataRecord(
                MetadataType.ProviderInfo,
                0, // Provider ID
                "TestProvider");
            providerInfo.Write(writer);
            
            // Provider section metadata
            var providerSection = new MetadataRecord(
                MetadataType.ProviderSection,
                0); // Provider ID
            providerSection.Write(writer);
            
            // Thread record
            var threadRecord = new ThreadRecord(1, 1000, 1001);
            threadRecord.Write(writer);
            
            // String records
            var categoryString = new StringRecord(1, "TestCategory");
            categoryString.Write(writer);
            
            var nameString = new StringRecord(2, "TestEvent");
            nameString.Write(writer);
            
            var argNameString = new StringRecord(3, "TestArg");
            argNameString.Write(writer);
            
            // Create arguments
            var numericArg = new NumericArgument(3, 42);
            
            // Event record with arguments
            var eventRecord = new EventRecord(
                EventType.Instant,
                1000000, // timestamp
                1, // thread ref
                1, // category ref
                2, // name ref
                [numericArg]);
            eventRecord.Write(writer);
            
            // Save to file
            File.WriteAllBytes(traceFilePath, ms.ToArray());
        }
        
        // Act
        bool isValid = _traceProcessor.ValidateTrace(traceFilePath);
        
        // Assert
        Assert.That(isValid, "Trace file should be valid");
    }

    [Test]
    public void CompleteEvent_ShouldProduceValidTrace()
    {
        // Arrange
        var traceFilePath = Path.Combine(_tempDirectory, "complete_event.ftf");
        
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            // Write magic number
            writer.Write(0x0016547846040010UL);
            
            // Provider info metadata
            var providerInfo = new MetadataRecord(
                MetadataType.ProviderInfo,
                0, // Provider ID
                "TestProvider");
            providerInfo.Write(writer);
            
            // Provider section metadata
            var providerSection = new MetadataRecord(
                MetadataType.ProviderSection,
                0); // Provider ID
            providerSection.Write(writer);
            
            // Thread record
            var threadRecord = new ThreadRecord(1, 1000, 1001);
            threadRecord.Write(writer);
            
            // String records
            var categoryString = new StringRecord(1, "TestCategory");
            categoryString.Write(writer);
            
            var nameString = new StringRecord(2, "TestEvent");
            nameString.Write(writer);
            
            var arg1NameString = new StringRecord(3, "Instructions");
            arg1NameString.Write(writer);
            
            var arg2NameString = new StringRecord(4, "Cycles");
            arg2NameString.Write(writer);
            
            // Create arguments
            var instructionsArg = new NumericArgument(3, 1000);
            var cyclesArg = new NumericArgument(4, 5000);
            
            // Complete duration event (with start and end time)
            var eventRecord = new EventRecord(
                EventType.DurationComplete,
                1000000, // start timestamp
                1, // thread ref
                1, // category ref
                2, // name ref
                [instructionsArg, cyclesArg],
                null, null, null,
                2000000); // end timestamp
            eventRecord.Write(writer);
            
            // Save to file
            File.WriteAllBytes(traceFilePath, ms.ToArray());
        }
        
        // Act
        bool isValid = _traceProcessor.ValidateTrace(traceFilePath);
        
        // Assert
        Assert.That(isValid, "Trace file should be valid");
    }

    [Test]
    public void SimpleSlice_ShouldProduceValidTrace()
    {
        //arrange
        var traceFilePath = Path.Combine("simple_slice.ftf");
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            
        }

    }
}