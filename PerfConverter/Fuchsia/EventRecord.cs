using System.Text;

namespace PerfConverter.Fuchsia;

public class EventRecord : Record
{
    private readonly EventType _eventType;
    private readonly byte _argumentCount;
    private readonly byte _threadRef;
    private readonly ushort _categoryStringRef;
    private readonly ushort _nameStringRef;
    private readonly ulong _timestamp;
    private readonly (ulong ProcessId, ulong ThreadId)? _inlineThread;
    private readonly string? _inlineCategory;
    private readonly string? _inlineName;
    private readonly RecordArgument[] _arguments;

    public EventRecord(
        EventType type,
        ulong timestamp,
        byte threadRef,
        ushort categoryRef,
        ushort nameRef,
        RecordArgument[]? arguments = null,
        (ulong, ulong)? inlineThread = null,
        string? inlineCategory = null,
        string? inlineName = null)
    {
        _eventType = type;
        _timestamp = timestamp;
        _threadRef = threadRef;
        _categoryStringRef = categoryRef;
        _nameStringRef = nameRef;
        _arguments = arguments ?? [];
        _argumentCount = (byte)_arguments.Length;
        _inlineThread = inlineThread;
        _inlineCategory = inlineCategory;
        _inlineName = inlineName;

        if (_argumentCount > 15)
            throw new ArgumentException("Maximum 15 arguments allowed per event");
    }

    protected override byte GetRecordType() => 4;

    protected override int GetRecordSizeInWords()
    {
        var size = 2; // Header + timestamp

        // Add inline thread if needed
        if (_inlineThread.HasValue)
            size += 2;

        // Add inline strings if needed
        if (_inlineCategory != null)
            size += AlignTo8Bytes(Encoding.UTF8.GetByteCount(_inlineCategory)) / WORD_SIZE;
        if (_inlineName != null)
            size += AlignTo8Bytes(Encoding.UTF8.GetByteCount(_inlineName)) / WORD_SIZE;

        // Add arguments size
        foreach (var arg in _arguments)
            size += arg.GetSizeInWords();

        return size;
    }

    protected override void WriteRecordData(BinaryWriter writer)
    {
        // Write event header
        var headerData =
            ((ulong)_eventType << 16) |
            ((ulong)_argumentCount << 20) |
            ((ulong)_threadRef << 24) |
            ((ulong)_categoryStringRef << 32) |
            ((ulong)_nameStringRef << 48);
        writer.Write(headerData);

        // Write timestamp
        writer.Write(_timestamp);

        // Write inline thread if needed
        if (_inlineThread.HasValue)
        {
            writer.Write(_inlineThread.Value.ProcessId);
            writer.Write(_inlineThread.Value.ThreadId);
        }

        // Write inline strings if needed
        if (_inlineCategory != null)
            WriteAlignedString(writer, _inlineCategory);
        if (_inlineName != null)
            WriteAlignedString(writer, _inlineName);

        // Write arguments
        foreach (var arg in _arguments)
            arg.Write(writer);
    }
}
