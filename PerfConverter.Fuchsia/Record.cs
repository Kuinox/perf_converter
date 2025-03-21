using System.Text;

namespace PerfConverter.Fuchsia;

public abstract class Record
{
    public const int WORD_SIZE = 8;  // 64 bits = 8 bytes

    // Every record must be 8-byte aligned
    public static int AlignTo8Bytes(int size) => (size + 7) & ~7;

    // Gets total size in 8-byte words
    protected abstract int GetRecordSizeInWords();

    // Gets record type (4 bits)
    protected abstract byte GetRecordType();

    // Write the record-specific data (48 bits in header + additional words)
    protected abstract void WriteRecordData(BinaryWriter writer);

    public void Write(BinaryWriter writer)
    {
        var recordSize = GetRecordSizeInWords();
        if (recordSize > 4095) // 12 bits max for normal records
            throw new InvalidOperationException("Record too large");

        if (recordSize == 0)
        {
            File.AppendAllText("log.txt", "Unexpected record of size 0\n");
            throw new InvalidOperationException("Unexpected record of size 0");
        }

        // Write header word:
        // [0..3]   - Record type (4 bits)
        // [4..15]  - Size in 8-byte words (12 bits)
        // [16..63] - Record specific data (48 bits)
        var header = (ulong)GetRecordType() | ((ulong)recordSize << 4);
        writer.Write(header);

        WriteRecordData(writer);
    }

    protected void WriteAlignedString(BinaryWriter writer, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        writer.Write(bytes);

        // Pad to 8-byte boundary
        var padding = (WORD_SIZE - (bytes.Length % WORD_SIZE)) % WORD_SIZE;
        if (padding > 0)
            writer.Write(new byte[padding]);
    }
}