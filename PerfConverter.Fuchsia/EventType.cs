namespace PerfConverter.Fuchsia;

public enum EventType : byte
{
    Instant = 0,
    Counter = 1,
    DurationBegin = 2,
    DurationEnd = 3,
    DurationComplete = 4,
    AsyncBegin = 5,
    AsyncInstant = 6,
    AsyncEnd = 7,
    FlowBegin = 8,
    FlowStep = 9,
    FlowEnd = 10
}