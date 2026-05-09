namespace PerfCapture;

public abstract record PerfControlChannel
{
    public static PerfControlChannel StandardInput() => new FileDescriptorPerfControlChannel(0);

    public static PerfControlChannel FileDescriptor(PerfFileDescriptor controlFileDescriptor, PerfFileDescriptor? acknowledgementFileDescriptor = null)
        => new FileDescriptorPerfControlChannel(controlFileDescriptor, acknowledgementFileDescriptor);

    public static PerfControlChannel Fifo(PerfControlFifoPath controlPath, PerfControlFifoPath? acknowledgementPath = null)
        => new FifoPerfControlChannel(controlPath, acknowledgementPath);
}
