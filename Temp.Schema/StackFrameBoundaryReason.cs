namespace Temp.Schema;

public enum StackFrameBoundaryReason : byte
{
    Call = 0,
    TraceResume = 1,
    Return = 2,
    TraceEnd = 3,
    AuxLoss = 4,
    EndOfInput = 5
}
