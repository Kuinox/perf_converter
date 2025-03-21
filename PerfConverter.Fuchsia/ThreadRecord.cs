namespace PerfConverter.Fuchsia;

public class ThreadRecord : Record
{
    private readonly byte _index;
    private readonly ulong _processId;
    private readonly ulong _threadId;

    public ThreadRecord(byte index, ulong processId, ulong threadId)
    {
        if (index == 0)
            throw new ArgumentException("Thread index 0 is reserved");
        _index = index;
        _processId = processId;
        _threadId = threadId;
    }

    protected override byte GetRecordType() => 3;

    protected override int GetRecordSizeInWords() => 3; // Header + ProcessId + ThreadId

    protected override void WriteRecordData(BinaryWriter writer)
    {
        var headerData = (ulong)_index << 16;
        writer.Write(headerData);
        writer.Write(_processId);
        writer.Write(_threadId);
    }
}
