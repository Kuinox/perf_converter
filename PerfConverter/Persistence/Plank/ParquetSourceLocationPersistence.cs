using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Temp.Schema.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed unsafe class ParquetSourceLocationPersistence : IDisposable
{
    const byte KeyStrengthAddressOnly = 0;
    const byte KeyStrengthDso = 1;
    const byte KeyStrengthBuildId = 2;

    readonly string _filePath;
    readonly Action<int>? _onFlush;
    readonly Action<int>? _onBuffered;
    readonly Dictionary<LocationKey, SourceLocationEntry> _entriesByKey = [];
    readonly List<SourceLocationEntry> _entries = [];
    readonly LlvmSourceLineResolver _sourceLineResolver = new();
    ulong _nextId = 1;
    bool _disposed;

    ParquetSourceLocationPersistence(string filePath, Action<int>? onFlush, Action<int>? onBuffered)
    {
        _filePath = filePath;
        _onFlush = onFlush;
        _onBuffered = onBuffered;
    }

    public ulong GetOrAdd(ResolvedLocation? location)
    {
        if (location == null)
            return 0;

        var buildId = location.BuildId;
        var dso = location.Dso;
        ReadOnlyMemory<byte>? symbol = location.Symbol.IsEmpty ? null : location.Symbol;
        var key = new LocationKey(
            ToKeyString(buildId),
            ToKeyString(dso),
            location.Address,
            ToKeyString(symbol.GetValueOrDefault()));

        if (_entriesByKey.TryGetValue(key, out var existing))
            return existing.Id;

        var sourceLine = _sourceLineResolver.Resolve(dso, location.Address);
        var entry = new SourceLocationEntry(
            Id: _nextId++,
            BuildId: buildId,
            Dso: dso,
            RelativeAddress: location.Address,
            Symbol: symbol,
            SymbolOffset: location.Symoff,
            SymbolStart: location.SymbolStart,
            SymbolEnd: location.SymbolEnd,
            SourceFileName: GetNullableUtf8(sourceLine.FileName),
            SourceLineNumber: sourceLine.LineNumber,
            SourceColumnNumber: sourceLine.ColumnNumber,
            InlineDepth: sourceLine.InlineDepth,
            KeyStrength: GetKeyStrength(buildId, dso),
            IsKernelIp: location.IsKernelIp);

        _entriesByKey.Add(key, entry);
        _entries.Add(entry);
        _onBuffered?.Invoke(1);
        return entry.Id;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _sourceLineResolver.Dispose();
        _entries.Sort(SourceLocationEntryComparer.Instance);

        using var fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = SourceLocationRowSchema.CreateRowWriter(fileStream, _onFlush, ParquetPersistenceOptions.WriterOptions);

        foreach (var entry in _entries)
        {
            var row = writer.GetRow();
            row.Id = entry.Id;
            row.BuildId = entry.BuildId;
            row.Dso = entry.Dso;
            row.RelativeAddress = entry.RelativeAddress;
            row.Symbol = entry.Symbol;
            row.SymbolOffset = entry.SymbolOffset;
            row.SymbolStart = entry.SymbolStart;
            row.SymbolEnd = entry.SymbolEnd;
            row.SourceFileName = entry.SourceFileName;
            row.SourceLineNumber = entry.SourceLineNumber;
            row.SourceColumnNumber = entry.SourceColumnNumber;
            row.InlineDepth = entry.InlineDepth;
            row.KeyStrength = entry.KeyStrength;
            row.IsKernelIp = entry.IsKernelIp;
            writer.Next();
        }

        writer.Complete();
        _disposed = true;
    }

    public static ParquetSourceLocationPersistence Create(
        string filePath,
        Action<int>? onFlush,
        Action<int>? onBuffered)
        => new(filePath, onFlush, onBuffered);

    static ReadOnlyMemory<byte>? GetNullableUtf8(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return EntryContentPool.Shared.GetByteMemory(Encoding.UTF8.GetBytes(value));
    }

    static byte GetKeyStrength(ReadOnlyMemory<byte> buildId, ReadOnlyMemory<byte> dso)
    {
        if (!buildId.IsEmpty)
            return KeyStrengthBuildId;

        return dso.IsEmpty ? KeyStrengthAddressOnly : KeyStrengthDso;
    }

    static string ToKeyString(ReadOnlyMemory<byte> value)
        => value.IsEmpty ? string.Empty : Convert.ToHexString(value.Span);

    readonly record struct LocationKey(
        string BuildId,
        string Dso,
        ulong RelativeAddress,
        string Symbol);

    sealed record SourceLocationEntry(
        ulong Id,
        ReadOnlyMemory<byte> BuildId,
        ReadOnlyMemory<byte> Dso,
        ulong RelativeAddress,
        ReadOnlyMemory<byte>? Symbol,
        uint SymbolOffset,
        ulong SymbolStart,
        ulong SymbolEnd,
        ReadOnlyMemory<byte>? SourceFileName,
        uint SourceLineNumber,
        uint SourceColumnNumber,
        uint InlineDepth,
        byte KeyStrength,
        byte IsKernelIp);

    sealed class SourceLocationEntryComparer : IComparer<SourceLocationEntry>
    {
        public static SourceLocationEntryComparer Instance { get; } = new();

        public int Compare(SourceLocationEntry? x, SourceLocationEntry? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;

            var buildIdComparison = x.BuildId.Span.SequenceCompareTo(y.BuildId.Span);
            if (buildIdComparison != 0)
                return buildIdComparison;

            var addressComparison = x.RelativeAddress.CompareTo(y.RelativeAddress);
            if (addressComparison != 0)
                return addressComparison;

            var dsoComparison = x.Dso.Span.SequenceCompareTo(y.Dso.Span);
            if (dsoComparison != 0)
                return dsoComparison;

            return x.Id.CompareTo(y.Id);
        }
    }

    readonly record struct SourceLine(string? FileName, uint LineNumber, uint ColumnNumber, uint InlineDepth);

    sealed class LlvmSourceLineResolver : IDisposable
    {
        readonly string[] _toolPaths = GetToolPaths();
        readonly Dictionary<string, SymbolizerProcess?> _symbolizers = new(StringComparer.Ordinal);
        bool _toolUnavailable;

        public SourceLine Resolve(ReadOnlyMemory<byte> dso, ulong relativeAddress)
        {
            if (_toolUnavailable || dso.IsEmpty)
                return default;

            var dsoPath = Encoding.UTF8.GetString(dso.Span);
            if (dsoPath.Length == 0 || dsoPath[0] == '[' || !File.Exists(dsoPath))
                return default;

            if (!_symbolizers.TryGetValue(dsoPath, out var symbolizer))
            {
                symbolizer = StartSymbolizer(dsoPath);
                _symbolizers.Add(dsoPath, symbolizer);
            }

            return symbolizer?.Resolve(relativeAddress) ?? default;
        }

        public void Dispose()
        {
            foreach (var symbolizer in _symbolizers.Values)
                symbolizer?.Dispose();

            _symbolizers.Clear();
        }

        SymbolizerProcess? StartSymbolizer(string dsoPath)
        {
            foreach (var toolPath in _toolPaths)
            {
                try
                {
                    return new SymbolizerProcess(toolPath, dsoPath);
                }
                catch (Win32Exception)
                {
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SOURCE_LOCATION_SYMBOLIZER_FAILED|Tool={toolPath}|Dso={dsoPath}|Error={ex.Message}");
                    return null;
                }
            }

            _toolUnavailable = true;
            return null;
        }

        static string[] GetToolPaths()
        {
            var configuredTool =
                Environment.GetEnvironmentVariable("PERFCONVERTER_LLVM_SYMBOLIZER") ??
                Environment.GetEnvironmentVariable("LLVM_SYMBOLIZER") ??
                Environment.GetEnvironmentVariable("PERFCONVERTER_LLVM_ADDR2LINE") ??
                Environment.GetEnvironmentVariable("LLVM_ADDR2LINE");

            return string.IsNullOrWhiteSpace(configuredTool)
                ? ["llvm-symbolizer", "llvm-addr2line"]
                : [configuredTool];
        }
    }

    sealed class SymbolizerProcess : IDisposable
    {
        readonly object _lock = new();
        readonly Process _process;
        bool _disposed;

        public SymbolizerProcess(string toolPath, string dsoPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("--output-style=GNU");
            startInfo.ArgumentList.Add("--functions=none");
            startInfo.ArgumentList.Add("--no-inlines");
            startInfo.ArgumentList.Add("--obj");
            startInfo.ArgumentList.Add(dsoPath);

            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start LLVM symbolizer.");
            _process.ErrorDataReceived += static (_, _) => { };
            _process.BeginErrorReadLine();
        }

        public SourceLine Resolve(ulong relativeAddress)
        {
            lock (_lock)
            {
                if (_disposed || _process.HasExited)
                    return default;

                try
                {
                    _process.StandardInput.WriteLine($"0x{relativeAddress:x}");
                    _process.StandardInput.Flush();
                    return ParseSourceLine(_process.StandardOutput.ReadLine());
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SOURCE_LOCATION_RESOLVE_FAILED|Address=0x{relativeAddress:x}|Error={ex.Message}");
                    Dispose();
                    return default;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(100))
                        _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup during perf shutdown.
            }
            finally
            {
                _process.Dispose();
                _disposed = true;
            }
        }

        static SourceLine ParseSourceLine(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return default;

            var value = output.Trim();
            var colonIndex = value.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex == value.Length - 1)
                return default;

            var fileName = value[..colonIndex];
            if (fileName == "??")
                return default;

            var numberStart = colonIndex + 1;
            var numberEnd = numberStart;
            while (numberEnd < value.Length && char.IsDigit(value[numberEnd]))
                numberEnd++;

            if (numberEnd == numberStart ||
                !uint.TryParse(value.AsSpan(numberStart, numberEnd - numberStart), NumberStyles.None, CultureInfo.InvariantCulture, out var lineNumber) ||
                lineNumber == 0)
            {
                return default;
            }

            return new SourceLine(fileName, lineNumber, ColumnNumber: 0, InlineDepth: 0);
        }
    }
}
