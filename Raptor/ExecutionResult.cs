using System;

namespace Raptor;

public enum VMStatus
{
    Success,
    Halted,
    OutOfMemory,
    DivisionByZero,
    StackOverflow,
    InvalidInstruction,
    GasExceeded,
    HostError
}

public struct ExecutionResult
{
    public VMStatus Status;
    public int IpOffset;
    public double[] RegistersSnapshot;
    public StackFrame[] CallStackSnapshot;
    public string? ErrorMessage;
}

public class VMPanicException : Exception
{
    public VMStatus Status { get; }
    public int IpOffset { get; }

    public VMPanicException(VMStatus status, int ipOffset, string message) : base(message)
    {
        Status = status;
        IpOffset = ipOffset;
    }
}
