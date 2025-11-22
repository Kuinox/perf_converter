using System.Runtime.InteropServices;
using System.Text;

namespace PerfConverter;

/// <summary>
/// Resolves addresses to symbols by parsing ELF symbol tables directly
/// Avoids calling perf's resolve_ip() which triggers addr2line deadlocks
/// </summary>
public unsafe class ElfSymbolResolver
{
    // Symbol information
    public record struct SymbolInfo(string Name, ulong Start, ulong End, string Dso);

    // Fast lookup: sorted list of symbols by start address
    private readonly List<SymbolInfo> _symbols = new();
    private bool _sorted = false;

    /// <summary>
    /// Load all symbols from JIT ELF files and other mapped libraries
    /// </summary>
    public void LoadSymbols(string[] elfPaths)
    {
        Console.Error.WriteLine($"Loading symbols from {elfPaths.Length} ELF files...");

        foreach (var path in elfPaths)
        {
            try
            {
                LoadElfFile(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load symbols from {path}: {ex.Message}");
            }
        }

        // Sort symbols by start address for binary search
        _symbols.Sort((a, b) => a.Start.CompareTo(b.Start));
        _sorted = true;

        Console.Error.WriteLine($"Loaded {_symbols.Count} symbols total");
    }

    /// <summary>
    /// Resolve an address to symbol information
    /// </summary>
    public SymbolInfo? Resolve(ulong address)
    {
        if (!_sorted || _symbols.Count == 0)
            return null;

        // Binary search for the symbol containing this address
        int left = 0;
        int right = _symbols.Count - 1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            var symbol = _symbols[mid];

            if (address >= symbol.Start && address < symbol.End)
                return symbol;

            if (address < symbol.Start)
                right = mid - 1;
            else
                left = mid + 1;
        }

        // Check the symbol just before our search position
        if (right >= 0 && right < _symbols.Count)
        {
            var symbol = _symbols[right];
            if (address >= symbol.Start && address < symbol.End)
                return symbol;
        }

        return null;
    }

    private void LoadElfFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs);

        // Read ELF header
        var magic = reader.ReadBytes(4);
        if (magic[0] != 0x7f || magic[1] != 'E' || magic[2] != 'L' || magic[3] != 'F')
            return; // Not an ELF file

        var elfClass = reader.ReadByte(); // 1 = 32-bit, 2 = 64-bit
        if (elfClass != 2) // We only support 64-bit
            return;

        reader.ReadByte(); // Endianness
        reader.ReadByte(); // ELF version
        fs.Seek(0x18, SeekOrigin.Begin); // Skip to e_shoff

        var sectionHeaderOffset = reader.ReadUInt64();
        fs.Seek(0x3a, SeekOrigin.Begin);
        var sectionHeaderSize = reader.ReadUInt16();
        var sectionHeaderCount = reader.ReadUInt16();
        var sectionNamesIndex = reader.ReadUInt16();

        // Read section headers
        var sections = new List<SectionHeader>();
        fs.Seek((long)sectionHeaderOffset, SeekOrigin.Begin);

        for (int i = 0; i < sectionHeaderCount; i++)
        {
            var section = new SectionHeader
            {
                NameOffset = reader.ReadUInt32(),
                Type = reader.ReadUInt32(),
                Flags = reader.ReadUInt64(),
                Addr = reader.ReadUInt64(),
                Offset = reader.ReadUInt64(),
                Size = reader.ReadUInt64(),
                Link = reader.ReadUInt32(),
                Info = reader.ReadUInt32(),
                AddrAlign = reader.ReadUInt64(),
                EntSize = reader.ReadUInt64()
            };
            sections.Add(section);
        }

        // Find .strtab and .symtab sections
        SectionHeader? symtab = null;
        SectionHeader? strtab = null;

        // Read section name string table
        var shstrtab = sections[sectionNamesIndex];
        var sectionNames = new byte[shstrtab.Size];
        fs.Seek((long)shstrtab.Offset, SeekOrigin.Begin);
        fs.Read(sectionNames, 0, (int)shstrtab.Size);

        for (int i = 0; i < sections.Count; i++)
        {
            var name = ReadNullTerminatedString(sectionNames, (int)sections[i].NameOffset);

            if (name == ".symtab")
                symtab = sections[i];
            else if (name == ".strtab")
                strtab = sections[i];
        }

        if (symtab == null || strtab == null)
            return; // No symbol table

        // Read string table
        var stringTable = new byte[strtab.Value.Size];
        fs.Seek((long)strtab.Value.Offset, SeekOrigin.Begin);
        fs.Read(stringTable, 0, (int)strtab.Value.Size);

        // Read symbol table
        fs.Seek((long)symtab.Value.Offset, SeekOrigin.Begin);
        var symbolCount = (int)(symtab.Value.Size / 24); // sizeof(Elf64_Sym) = 24

        for (int i = 0; i < symbolCount; i++)
        {
            var nameOffset = reader.ReadUInt32();
            var info = reader.ReadByte();
            var other = reader.ReadByte();
            var shndx = reader.ReadUInt16();
            var value = reader.ReadUInt64();
            var size = reader.ReadUInt64();

            // Only include function symbols (STT_FUNC)
            var type = info & 0xf;
            if (type != 2) // STT_FUNC
                continue;

            if (size == 0 || value == 0)
                continue;

            var symbolName = ReadNullTerminatedString(stringTable, (int)nameOffset);
            if (string.IsNullOrEmpty(symbolName))
                continue;

            _symbols.Add(new SymbolInfo(
                Name: symbolName,
                Start: value,
                End: value + size,
                Dso: Path.GetFileName(path)
            ));
        }
    }

    private string ReadNullTerminatedString(byte[] buffer, int offset)
    {
        if (offset >= buffer.Length)
            return "";

        int end = offset;
        while (end < buffer.Length && buffer[end] != 0)
            end++;

        return Encoding.UTF8.GetString(buffer, offset, end - offset);
    }

    private struct SectionHeader
    {
        public uint NameOffset;
        public uint Type;
        public ulong Flags;
        public ulong Addr;
        public ulong Offset;
        public ulong Size;
        public uint Link;
        public uint Info;
        public ulong AddrAlign;
        public ulong EntSize;
    }
}
