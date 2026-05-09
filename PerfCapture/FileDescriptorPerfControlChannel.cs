namespace PerfCapture;

public sealed record FileDescriptorPerfControlChannel(
    PerfFileDescriptor ControlFileDescriptor,
    PerfFileDescriptor? AcknowledgementFileDescriptor = null) : PerfControlChannel;
