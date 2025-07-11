using Microsoft.LinuxTracepoints.Decode;

namespace CLI;

class AuxDataExtractor
{
    public static void Process(string inputPath, Action<AuxDataEntry?> onEntry)
    {
        using var reader = new PerfDataFileReader();
        if (!reader.OpenFile(inputPath, PerfDataFileEventOrder.File))
            throw new InvalidDataException("Failed to open perf data file");

        ProcessEvents(reader, onEntry);
    }

    private static void ProcessEvents(PerfDataFileReader reader, Action<AuxDataEntry?> onEntry)
    {
        while (true)
        {
            var result = reader.ReadEvent(out var perfEvent);

            if (result != PerfDataFileResult.Ok)
            {
                if (result == PerfDataFileResult.EndOfFile)
                    return;
                throw new InvalidOperationException($"Error reading perf data file: {result}");
            }

            if (perfEvent.Header.Type != PerfEventHeaderType.Aux)
            {
                onEntry(null);
                continue;
            }

            var eventResult = reader.GetNonSampleEventInfo(perfEvent, out var info);
            if (eventResult != PerfDataFileResult.Ok)
                throw new InvalidOperationException($"Error reading perf data file: {eventResult}");

            var flagBytes = info.BytesSpan.Slice(24, 8);

            onEntry(new AuxDataEntry
            {
                Time = info.Time,
                Pid = info.Pid,
                Tid = info.Tid,
                Cpu = info.Cpu,
                Flags = BitConverter.ToUInt64(flagBytes)
            });
        }
    }
}