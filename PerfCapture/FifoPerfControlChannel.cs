namespace PerfCapture;

public sealed record FifoPerfControlChannel(
    PerfControlFifoPath ControlPath,
    PerfControlFifoPath? AcknowledgementPath = null) : PerfControlChannel;
