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
        
        // Create records to be processed
        var providerInfo = new MetadataRecord(
            MetadataType.ProviderInfo,
            0, // Provider ID
            "TestProvider");
            
        var providerSection = new MetadataRecord(
            MetadataType.ProviderSection,
            0); // Provider ID
            
        var threadRecord = new ThreadRecord(1, 1000, 1001);
            
        var categoryString = new StringRecord(1, "TestCategory");
        var nameString = new StringRecord(2, "TestEvent");
            
        var eventBegin = new EventRecord(
            EventType.DurationBegin,
            1000000, // timestamp
            1, // thread ref
            1, // category ref
            2); // name ref
            
        var eventEnd = new EventRecord(
            EventType.DurationEnd,
            2000000, // timestamp
            1, // thread ref
            1, // category ref
            2); // name ref
        
        // Use trace processor to create the file
        var records = new Record[] 
        { 
            providerInfo, 
            providerSection, 
            threadRecord, 
            categoryString, 
            nameString, 
            eventBegin, 
            eventEnd 
        };
        
        _traceProcessor.WriteTraceFile(traceFilePath, records);
        
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
        
        // Create records to be processed
        var providerInfo = new MetadataRecord(
            MetadataType.ProviderInfo,
            0, // Provider ID
            "TestProvider");
            
        var providerSection = new MetadataRecord(
            MetadataType.ProviderSection,
            0); // Provider ID
            
        var threadRecord = new ThreadRecord(1, 1000, 1001);
            
        var categoryString = new StringRecord(1, "TestCategory");
        var nameString = new StringRecord(2, "TestEvent");
        var argNameString = new StringRecord(3, "TestArg");
            
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
        
        // Use trace processor to create the file
        var records = new Record[] 
        { 
            providerInfo, 
            providerSection, 
            threadRecord, 
            categoryString, 
            nameString, 
            argNameString, 
            eventRecord 
        };
        
        _traceProcessor.WriteTraceFile(traceFilePath, records);
        
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
        
        // Create records to be processed
        var providerInfo = new MetadataRecord(
            MetadataType.ProviderInfo,
            0, // Provider ID
            "TestProvider");
            
        var providerSection = new MetadataRecord(
            MetadataType.ProviderSection,
            0); // Provider ID
            
        var threadRecord = new ThreadRecord(1, 1000, 1001);
            
        var categoryString = new StringRecord(1, "TestCategory");
        var nameString = new StringRecord(2, "TestEvent");
        var arg1NameString = new StringRecord(3, "Instructions");
        var arg2NameString = new StringRecord(4, "Cycles");
            
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
        
        // Use trace processor to create the file
        var records = new Record[] 
        { 
            providerInfo, 
            providerSection, 
            threadRecord, 
            categoryString, 
            nameString, 
            arg1NameString, 
            arg2NameString, 
            eventRecord 
        };
        
        _traceProcessor.WriteTraceFile(traceFilePath, records);
        
        // Act
        bool isValid = _traceProcessor.ValidateTrace(traceFilePath);
        
        // Assert
        Assert.That(isValid, "Trace file should be valid");
    }

    [Test]
    public void SimpleSlice_ShouldProduceValidTrace()
    {
        // Arrange
        var traceFilePath = Path.Combine(_tempDirectory, "simple_slice.ftf");
        
        // Create records to be processed
        var providerInfo = new MetadataRecord(
            MetadataType.ProviderInfo,
            0, // Provider ID
            "TestProvider");
            
        var providerSection = new MetadataRecord(
            MetadataType.ProviderSection,
            0); // Provider ID
            
        var threadRecord = new ThreadRecord(1, 1000, 1001);
            
        var categoryString = new StringRecord(1, "TestCategory");
        var nameString = new StringRecord(2, "TestSlice");
        
        // Create a slice event using DurationComplete instead of separate begin/end
        // DurationComplete is more likely to be recognized as a proper slice by Perfetto
        var sliceEvent = new EventRecord(
            EventType.DurationComplete,  // Use DurationComplete instead of separate begin/end
            1000000, // start timestamp
            1, // thread ref
            1, // category ref
            2, // name ref
            null, null, null, null,
            2000000); // end timestamp
        
        // Use trace processor to create the file
        var records = new Record[] 
        { 
            providerInfo, 
            providerSection, 
            threadRecord, 
            categoryString, 
            nameString, 
            sliceEvent
        };
        
        _traceProcessor.WriteTraceFile(traceFilePath, records);
        
        Console.WriteLine($"Trace file: {traceFilePath}");
        
        // Act
        bool isValid = _traceProcessor.ValidateTrace(traceFilePath);
        bool hasSlices = _traceProcessor.VerifySlices(traceFilePath);
        
        // Assert
        Assert.That(isValid, "Trace file should be valid");
        Assert.That(hasSlices, "Trace file should contain slices");
    }
}