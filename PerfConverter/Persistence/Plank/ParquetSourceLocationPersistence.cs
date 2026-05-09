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
    readonly SymbolNameResolver _symbolNameResolver = new();
    readonly object _gate = new();
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

        lock (_gate)
        {
            var buildId = location.BuildId;
            var dso = location.Dso;
            var key = new LocationKey(
                ToKeyString(buildId),
                ToKeyString(dso),
                location.Address);

            if (_entriesByKey.TryGetValue(key, out var existing))
                return existing.Id;

            var sourceLine = _sourceLineResolver.Resolve(dso, location.Address);
            var symbol = GetSymbol(dso, location);
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
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _sourceLineResolver.Dispose();

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

    ReadOnlyMemory<byte>? GetSymbol(ReadOnlyMemory<byte> dso, ResolvedLocation location)
    {
        var symbolName = _symbolNameResolver.Resolve(dso, location.Address);
        if (!string.IsNullOrEmpty(symbolName))
            return GetNullableUtf8(symbolName);

        return location.Symbol.IsEmpty ? null : location.Symbol;
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
        ulong RelativeAddress);

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

    readonly record struct SourceLine(string? FileName, uint LineNumber, uint ColumnNumber, uint InlineDepth);

    sealed class SymbolNameResolver
    {
        readonly string[] _toolPaths = GetToolPaths();
        readonly Dictionary<string, SymbolTable?> _tables = new(StringComparer.Ordinal);
        bool _toolUnavailable;

        public string? Resolve(ReadOnlyMemory<byte> dso, ulong relativeAddress)
        {
            if (_toolUnavailable || dso.IsEmpty)
                return null;

            var dsoPath = Encoding.UTF8.GetString(dso.Span);
            if (dsoPath.Length == 0 || dsoPath[0] == '[' || !File.Exists(dsoPath))
                return null;

            if (!_tables.TryGetValue(dsoPath, out var table))
            {
                table = LoadTable(dsoPath);
                _tables.Add(dsoPath, table);
            }

            return table?.Resolve(relativeAddress);
        }

        SymbolTable? LoadTable(string dsoPath)
        {
            foreach (var toolPath in _toolPaths)
            {
                try
                {
                    return SymbolTable.Load(toolPath, dsoPath);
                }
                catch (Win32Exception)
                {
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SOURCE_LOCATION_SYMBOL_TABLE_FAILED|Tool={toolPath}|Dso={dsoPath}|Error={ex.Message}");
                    return null;
                }
            }

            _toolUnavailable = true;
            return null;
        }

        static string[] GetToolPaths()
        {
            var configuredTool =
                Environment.GetEnvironmentVariable("PERFCONVERTER_NM") ??
                Environment.GetEnvironmentVariable("LLVM_NM") ??
                Environment.GetEnvironmentVariable("NM");

            return string.IsNullOrWhiteSpace(configuredTool)
                ? ["llvm-nm", "nm"]
                : [configuredTool];
        }
    }

    sealed class SymbolTable
    {
        readonly SymbolEntry[] _entries;

        SymbolTable(SymbolEntry[] entries)
            => _entries = entries;

        public string? Resolve(ulong relativeAddress)
        {
            var low = 0;
            var high = _entries.Length - 1;
            var best = -1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (_entries[middle].Address <= relativeAddress)
                {
                    best = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return best < 0 ? null : _entries[best].Name;
        }

        public static SymbolTable? Load(string toolPath, string dsoPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = toolPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.StartInfo.ArgumentList.Add("-n");
            process.StartInfo.ArgumentList.Add("--defined-only");
            process.StartInfo.ArgumentList.Add(dsoPath);
            process.Start();

            var entries = new List<SymbolEntry>();
            while (process.StandardOutput.ReadLine() is { } line)
            {
                if (TryParseSymbol(line, out var entry))
                    entries.Add(entry);
            }

            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            var ordered = entries
                .GroupBy(static entry => entry.Address)
                .Select(static group => group.First())
                .OrderBy(static entry => entry.Address)
                .ToArray();

            return ordered.Length == 0 ? null : new SymbolTable(ordered);
        }

        static bool TryParseSymbol(string line, out SymbolEntry entry)
        {
            entry = default;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 ||
                !ulong.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ||
                !IsCodeSymbol(parts[1]))
            {
                return false;
            }

            entry = new SymbolEntry(address, parts[2]);
            return true;
        }

        static bool IsCodeSymbol(string type)
            => type.Length == 1 && type[0] is 'T' or 't' or 'W' or 'w';
    }

    readonly record struct SymbolEntry(ulong Address, string Name);

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
