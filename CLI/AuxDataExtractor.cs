using Microsoft.LinuxTracepoints.Decode;

namespace CLI;

static class AuxDataExtractor
{
    public static void Process(string inputPath, Action<AuxDataEntry?> onEntry)
    {
        var perfDataPath = ResolvePerfDataFile(inputPath);
        using var reader = new PerfDataFileReader();
        if (!reader.OpenFile(perfDataPath, PerfDataFileEventOrder.File))
            throw new InvalidDataException($"Failed to open perf data file: {perfDataPath}");

        ProcessEvents(reader, onEntry);
    }

    static string ResolvePerfDataFile(string inputPath)
    {
        if (File.Exists(inputPath))
            return inputPath;

        if (!Directory.Exists(inputPath))
            return inputPath;

        var dataPath = Path.Combine(inputPath, "data");
        return File.Exists(dataPath) ? dataPath : inputPath;
    }

    static void ProcessEvents(PerfDataFileReader reader, Action<AuxDataEntry?> onEntry)
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
                throw new InvalidOperationException($"Error reading aux data event: {eventResult}");

            var flags = info.BytesSpan.Length >= 32
                ? BitConverter.ToUInt64(info.BytesSpan.Slice(24, 8))
                : 0;

            onEntry(new AuxDataEntry(
                Time: info.Time,
                Pid: info.Pid,
                Tid: info.Tid,
                Cpu: info.Cpu,
                Flags: flags));
        }
    }
}
