// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
var jitDump = Environment.GetEnvironmentVariable("JITDUMP_USE_ARCH_TIMESTAMP");
Console.WriteLine($"JITDUMP_USE_ARCH_TIMESTAMP:{jitDump}");