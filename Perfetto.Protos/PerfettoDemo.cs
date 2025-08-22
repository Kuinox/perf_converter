using Perfetto.Protos;
using Google.Protobuf;

namespace Perfetto.Protos.Tests;

/// <summary>
/// Simple demonstration of using the generated Perfetto C# classes
/// </summary>
public class PerfettoDemo
{
    public static void DemoSystemInfo()
    {
        // Create a SystemInfo object
        var systemInfo = new SystemInfo
        {
            Utsname = new Utsname
            {
                Sysname = "Linux",
                Version = "5.4.0-1234-azure",
                Release = "Ubuntu 20.04.5 LTS",
                Machine = "x86_64"
            }
        };

        // Serialize to protobuf bytes
        var bytes = systemInfo.ToByteArray();
        
        // Deserialize back
        var deserializedSystemInfo = SystemInfo.Parser.ParseFrom(bytes);
        
        Console.WriteLine($"System: {deserializedSystemInfo.Utsname.Sysname} {deserializedSystemInfo.Utsname.Release}");
        Console.WriteLine($"Architecture: {deserializedSystemInfo.Utsname.Machine}");
        Console.WriteLine($"Serialized size: {bytes.Length} bytes");
    }
    
    public static void DemoTraceUuid()
    {
        // Create a TraceUuid object
        var traceUuid = new TraceUuid
        {
            Lsb = 123456789123456789L,
            Msb = 987654321987654321L
        };
        
        var bytes = traceUuid.ToByteArray();
        var deserialized = TraceUuid.Parser.ParseFrom(bytes);
        
        Console.WriteLine($"Trace UUID: MSB={deserialized.Msb}, LSB={deserialized.Lsb}");
    }
    
    public static void DemoProcessTree()
    {
        // Create a ProcessTree with process information
        var processTree = new ProcessTree();
        
        var process = new ProcessTree.Types.Process
        {
            Pid = 1234,
            Ppid = 1,
            Uid = 1000
        };
        
        // Add command line arguments
        process.Cmdline.Add("my-application");
        process.Cmdline.Add("--config");
        process.Cmdline.Add("production");
        
        processTree.Processes.Add(process);
        
        var bytes = processTree.ToByteArray();
        var deserialized = ProcessTree.Parser.ParseFrom(bytes);
        
        Console.WriteLine($"Process Tree: {deserialized.Processes.Count} processes");
        var proc = deserialized.Processes[0];
        Console.WriteLine($"  PID: {proc.Pid}, PPID: {proc.Ppid}");
        Console.WriteLine($"  Command: {string.Join(" ", proc.Cmdline)}");
    }
}