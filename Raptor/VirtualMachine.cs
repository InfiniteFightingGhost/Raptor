using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Raptor;

public unsafe class VirtualMachine
{
    private uint[] _instructions = null!;
    private double[] _constants = null!;
    int BasePtr = 0;
    private int[] _breakpoints = null!;
    private uint[] _methods = null!;
    private static readonly int _heapSize = 16 * 1024 * 1024;
    private uint _heapHeader = 0;
    private byte[] _heap = new byte[_heapSize];
    private uint _rngState = 2463534215; // RNG seed
    private char[] _outBuffer = new char[65536];

    private readonly Dictionary<ushort, HostFFIDelegate> _registeredHostMethods = new();

    public void RegisterHostMethod(ushort methodIndex, HostFFIDelegate del)
    {
        _registeredHostMethods[methodIndex] = del;
    }

    public delegate void HostFFIDelegate(ref VMState state);

    public Span<double> GetDoubleSpan(double registerValue, int count) =>
        new Span<double>((void*)(ulong)registerValue, count);

    public Span<byte> GetByteSpan(double registerValue, int count) =>
        new Span<byte>((void*)(ulong)registerValue, count);

    public Span<double> GetDoubleSpanFromOffset(uint offset, int count)
    {
        fixed (byte* heapPtr = _heap)
            return new Span<double>(heapPtr + offset, count);
    }

    public Span<byte> GetByteSpanFromOffset(uint offset, int count)
    {
        fixed (byte* heapPtr = _heap)
            return new Span<byte>(heapPtr + offset, count);
    }

    public void LoadProgram(VMChunk chunk, int[] breakpoints)
    {
        _instructions = chunk.Instructions;
        _constants = chunk.Constants;
        _methods = chunk.MethodTable;

        foreach (var pair in _registeredHostMethods)
        {
            if ((_methods[pair.Key] & 0x80000000) == 0)
            {
                _methods[pair.Key] = pair.Key | 0x80000000;
            }
        }

        BasePtr = 0;
        _breakpoints = breakpoints;
    }

    private unsafe void DumpRegisters(double* registers, int count = 32)
    {
        for (int i = 0; i < count; i++)
        {
            Console.Write($"R{i:D2}: {registers[i]:G} | ");

            if ((i + 1) % 2 == 0)
                Console.WriteLine();
        }
    }

    public unsafe ExecutionResult RunFast()
    {
        double* RegPtr = stackalloc double[256];
        Unsafe.InitBlockUnaligned(RegPtr, 0, 256 * sizeof(double));

        StackFrame* framePtr = stackalloc StackFrame[32];
        fixed (uint* instPtr = _instructions)
        fixed (double* constPtr = _constants)
        fixed (uint* methodTablePtr = _methods)
        fixed (byte* heapPtr = _heap)
        fixed (char* outBufferPtr = _outBuffer)
        {
            *(uint*)heapPtr = 0xFFFFFFFF;
            *(uint*)(heapPtr + 4) = (uint)_heapSize;
            Console.Error.WriteLine("Starting VM...");
            VMState state = new VMState
            {
                RegPtr = RegPtr,
                ConstPtr = constPtr,
                MethodTablePtr = methodTablePtr,
                InstPtr = instPtr,
                Ip = instPtr,
                HeapPtr = heapPtr,
                FreeBlockHeaderPointer = _heapHeader,
                BasePtr = 0,
                CallStackPtr = framePtr,
                CallStackLimit = framePtr + 32,
                RngState = _rngState,
                OutBufferPtr = outBufferPtr,
                OutBufferCapacity = _outBuffer.Length,
                OutBufferOffset = 0,
            };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    Instruction instruction = new Instruction(*state.Ip++);
#if DEBUG_SLOW
                    if (_breakpoints.Contains((int)state.Ip - (int)state.InstPtr))
                    {
                        DumpRegisters(state.RegPtr);
                        Console.ReadLine();
                    }
                    Thread.Sleep(50);
                    Console.WriteLine(
                        $"[TRACE] PC:{(state.Ip - state.InstPtr):D4} | Op:{instruction.Op, -8} | A:{instruction.A, -3} | B:{instruction.B, -3} | C:{instruction.C, -3} | R45:{Reg(state.RegPtr, state.BasePtr, 45)} | R46:{Reg(state.RegPtr, state.BasePtr, 46)} | R47:{Reg(state.RegPtr, state.BasePtr, 47)} | R59:{Reg(state.RegPtr, state.BasePtr, 59)} | R60:{Reg(state.RegPtr, state.BasePtr, 60)} | R61:{Reg(state.RegPtr, state.BasePtr, 61)}"
                    );
#endif
                    switch (instruction.Op)
                    {
                        case OpCode.LOADC:
                            ExecuteLoadC(instruction, ref state);
                            break;
                        case OpCode.MOVE:
                            ExecuteMove(instruction, ref state);
                            break;
                        case OpCode.UNM:
                            ExecuteUnm(instruction, ref state);
                            break;
                        case OpCode.SWAP:
                            ExecuteSwap(instruction, ref state);
                            break;
                        case OpCode.ADD:
                            ExecuteAdd(instruction, ref state);
                            break;
                        case OpCode.SUB:
                            ExecuteSub(instruction, ref state);
                            break;
                        case OpCode.MUL:
                            ExecuteMul(instruction, ref state);
                            break;
                        case OpCode.DIV:
                            ExecuteDiv(instruction, ref state);
                            break;
                        case OpCode.POW:
                            ExecutePow(instruction, ref state);
                            break;
                        case OpCode.SQRT:
                            ExecuteSqrt(instruction, ref state);
                            break;
                        case OpCode.FISR:
                            ExecuteFisr(instruction, ref state);
                            break;
                        case OpCode.JUMP:
                            ExecuteJump(instruction, ref state);
                            break;
                        case OpCode.CALL:
                            ExecuteCallOrFFI(instruction, ref state, this);
                            break;
                        case OpCode.RETURN:
                            ExecuteReturn(instruction, ref state);
                            break;
                        case OpCode.PRINT:
                            ExecutePrint(instruction, ref state);
                            break;
                        case OpCode.PRINTA:
                            ExecutePrintA(instruction, ref state);
                            break;
                        case OpCode.EQ:
                            ExecuteEq(instruction, ref state);
                            break;
                        case OpCode.LT:
                            ExecuteLt(instruction, ref state);
                            break;
                        case OpCode.LE:
                            ExecuteLe(instruction, ref state);
                            break;
                        case OpCode.HALT:
                            stopwatch.Stop();
                            ExecuteHalt(instruction, ref state);
                            _rngState = state.RngState;
                            Console.Error.WriteLine(
                                $"Execution time:{stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedTicks} ticks, {stopwatch.Elapsed.TotalMicroseconds:F1} us)"
                            );

                            double[] regSnapshot = new double[256];
                            new ReadOnlySpan<double>(RegPtr, 256).CopyTo(regSnapshot);

                            int stackCount = (int)(state.CallStackPtr - framePtr);
                            StackFrame[] stackSnapshot = new StackFrame[stackCount];
                            for (int i = 0; i < stackCount; i++)
                            {
                                stackSnapshot[i] = framePtr[i];
                            }

                            return new ExecutionResult
                            {
                                Status = VMStatus.Halted,
                                IpOffset = (int)(state.Ip - state.InstPtr - 1),
                                RegistersSnapshot = regSnapshot,
                                CallStackSnapshot = stackSnapshot,
                                ErrorMessage = null,
                            };
                        case OpCode.RAND:
                            ExecuteRand(instruction, ref state);
                            break;
                        case OpCode.FOR:
                            ExecuteFor(instruction, ref state);
                            break;
                        case OpCode.NEWARR:
                            ExecuteNewArray(instruction, ref state);
                            break;
                        case OpCode.SETARR:
                            ExecuteSetArray(instruction, ref state);
                            break;
                        case OpCode.SETARRA:
                            ExecuteSetArrayASCII(instruction, ref state);
                            break;
                        case OpCode.GETARR:
                            ExecuteGetArray(instruction, ref state);
                            break;
                        case OpCode.GETARRA:
                            ExecuteGetArrayASCII(instruction, ref state);
                            break;
                        case OpCode.FREEARR:
                            ExecuteFreeArray(instruction, ref state);
                            break;
                        case OpCode.BINAND:
                            ExecuteBinaryAnd(instruction, ref state);
                            break;
                        case OpCode.BINOR:
                            ExecuteBinaryOr(instruction, ref state);
                            break;
                        case OpCode.BINXOR:
                            ExecuteBinaryXor(instruction, ref state);
                            break;
                        case OpCode.BINLSH:
                            ExecuteBinaryLeftShift(instruction, ref state);
                            break;
                        case OpCode.BINRSH:
                            ExecuteBinaryRightShift(instruction, ref state);
                            break;
                    }
                }
            }
            catch (VMPanicException ex)
            {
                stopwatch.Stop();
                _rngState = state.RngState;

                double[] regSnapshot = new double[256];
                new ReadOnlySpan<double>(RegPtr, 256).CopyTo(regSnapshot);

                int stackCount = (int)(state.CallStackPtr - framePtr);
                StackFrame[] stackSnapshot = new StackFrame[stackCount];
                for (int i = 0; i < stackCount; i++)
                {
                    stackSnapshot[i] = framePtr[i];
                }

                return new ExecutionResult
                {
                    Status = ex.Status,
                    IpOffset = ex.IpOffset,
                    RegistersSnapshot = regSnapshot,
                    CallStackSnapshot = stackSnapshot,
                    ErrorMessage = ex.Message,
                };
            }
        }
    }

    public delegate void DebugHook(ref VMState state, Instruction instruction);

    public unsafe ExecutionResult RunDebug(DebugHook onInstructionExecuted)
    {
        double* RegPtr = stackalloc double[256];
        Unsafe.InitBlockUnaligned(RegPtr, 0, 256 * sizeof(double));

        StackFrame* framePtr = stackalloc StackFrame[32];
        fixed (uint* instPtr = _instructions)
        fixed (double* constPtr = _constants)
        fixed (uint* methodTablePtr = _methods)
        fixed (byte* heapPtr = _heap)
        fixed (char* outBufferPtr = _outBuffer)
        {
            *(uint*)heapPtr = 0xFFFFFFFF;
            *(uint*)(heapPtr + 4) = (uint)_heapSize;
            Console.Error.WriteLine("Starting VM in Debug Mode...");
            VMState state = new VMState
            {
                RegPtr = RegPtr,
                ConstPtr = constPtr,
                MethodTablePtr = methodTablePtr,
                InstPtr = instPtr,
                Ip = instPtr,
                HeapPtr = heapPtr,
                FreeBlockHeaderPointer = _heapHeader,
                BasePtr = 0,
                CallStackPtr = framePtr,
                CallStackLimit = framePtr + 32,
                RngState = _rngState,
                OutBufferPtr = outBufferPtr,
                OutBufferCapacity = _outBuffer.Length,
                OutBufferOffset = 0,
            };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    Instruction instruction = new Instruction(*state.Ip++);

                    onInstructionExecuted?.Invoke(ref state, instruction);

                    switch (instruction.Op)
                    {
                        case OpCode.LOADC:
                            ExecuteLoadC(instruction, ref state);
                            break;
                        case OpCode.MOVE:
                            ExecuteMove(instruction, ref state);
                            break;
                        case OpCode.UNM:
                            ExecuteUnm(instruction, ref state);
                            break;
                        case OpCode.SWAP:
                            ExecuteSwap(instruction, ref state);
                            break;
                        case OpCode.ADD:
                            ExecuteAdd(instruction, ref state);
                            break;
                        case OpCode.SUB:
                            ExecuteSub(instruction, ref state);
                            break;
                        case OpCode.MUL:
                            ExecuteMul(instruction, ref state);
                            break;
                        case OpCode.DIV:
                            ExecuteDiv(instruction, ref state);
                            break;
                        case OpCode.POW:
                            ExecutePow(instruction, ref state);
                            break;
                        case OpCode.SQRT:
                            ExecuteSqrt(instruction, ref state);
                            break;
                        case OpCode.FISR:
                            ExecuteFisr(instruction, ref state);
                            break;
                        case OpCode.JUMP:
                            ExecuteJump(instruction, ref state);
                            break;
                        case OpCode.CALL:
                            ExecuteCallOrFFI(instruction, ref state, this);
                            break;
                        case OpCode.RETURN:
                            ExecuteReturn(instruction, ref state);
                            break;
                        case OpCode.PRINT:
                            ExecutePrint(instruction, ref state);
                            break;
                        case OpCode.PRINTA:
                            ExecutePrintA(instruction, ref state);
                            break;
                        case OpCode.EQ:
                            ExecuteEq(instruction, ref state);
                            break;
                        case OpCode.LT:
                            ExecuteLt(instruction, ref state);
                            break;
                        case OpCode.LE:
                            ExecuteLe(instruction, ref state);
                            break;
                        case OpCode.HALT:
                            stopwatch.Stop();
                            ExecuteHalt(instruction, ref state);
                            _rngState = state.RngState;
                            Console.Error.WriteLine(
                                $"Debug Execution time:{stopwatch.ElapsedMilliseconds} ms ({stopwatch.ElapsedTicks} ticks, {stopwatch.Elapsed.TotalMicroseconds:F1} us)"
                            );

                            double[] regSnapshot = new double[256];
                            new ReadOnlySpan<double>(RegPtr, 256).CopyTo(regSnapshot);

                            int stackCount = (int)(state.CallStackPtr - framePtr);
                            StackFrame[] stackSnapshot = new StackFrame[stackCount];
                            for (int i = 0; i < stackCount; i++)
                            {
                                stackSnapshot[i] = framePtr[i];
                            }

                            return new ExecutionResult
                            {
                                Status = VMStatus.Halted,
                                IpOffset = (int)(state.Ip - state.InstPtr - 1),
                                RegistersSnapshot = regSnapshot,
                                CallStackSnapshot = stackSnapshot,
                                ErrorMessage = null,
                            };
                        case OpCode.RAND:
                            ExecuteRand(instruction, ref state);
                            break;
                        case OpCode.FOR:
                            ExecuteFor(instruction, ref state);
                            break;
                        case OpCode.NEWARR:
                            ExecuteNewArray(instruction, ref state);
                            break;
                        case OpCode.SETARR:
                            ExecuteSetArray(instruction, ref state);
                            break;
                        case OpCode.SETARRA:
                            ExecuteSetArrayASCII(instruction, ref state);
                            break;
                        case OpCode.GETARR:
                            ExecuteGetArray(instruction, ref state);
                            break;
                        case OpCode.GETARRA:
                            ExecuteGetArrayASCII(instruction, ref state);
                            break;
                        case OpCode.FREEARR:
                            ExecuteFreeArray(instruction, ref state);
                            break;
                        case OpCode.BINAND:
                            ExecuteBinaryAnd(instruction, ref state);
                            break;
                        case OpCode.BINOR:
                            ExecuteBinaryOr(instruction, ref state);
                            break;
                        case OpCode.BINXOR:
                            ExecuteBinaryXor(instruction, ref state);
                            break;
                        case OpCode.BINLSH:
                            ExecuteBinaryLeftShift(instruction, ref state);
                            break;
                        case OpCode.BINRSH:
                            ExecuteBinaryRightShift(instruction, ref state);
                            break;
                    }
                }
            }
            catch (VMPanicException ex)
            {
                stopwatch.Stop();
                _rngState = state.RngState;

                double[] regSnapshot = new double[256];
                new ReadOnlySpan<double>(RegPtr, 256).CopyTo(regSnapshot);

                int stackCount = (int)(state.CallStackPtr - framePtr);
                StackFrame[] stackSnapshot = new StackFrame[stackCount];
                for (int i = 0; i < stackCount; i++)
                {
                    stackSnapshot[i] = framePtr[i];
                }

                return new ExecutionResult
                {
                    Status = ex.Status,
                    IpOffset = ex.IpOffset,
                    RegistersSnapshot = regSnapshot,
                    CallStackSnapshot = stackSnapshot,
                    ErrorMessage = ex.Message,
                };
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CallStackPush(ref StackFrame* stackFramePtr, StackFrame frame)
    {
        *stackFramePtr = frame;
        stackFramePtr++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static StackFrame CallStackPop(ref StackFrame* stackFramePtr)
    {
        stackFramePtr--;
        StackFrame frame = *stackFramePtr;
        return frame;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe ref double Reg(double* RegPtr, int BasePtr, uint index)
    {
        return ref RegPtr[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteLoadC(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        uint constantIndex = instruction.Bx;
        Reg(state.RegPtr, state.BasePtr, a) = state.ConstPtr[constantIndex];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteMove(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        byte b = (byte)instruction.B;
        Reg(state.RegPtr, state.BasePtr, a) = Reg(state.RegPtr, state.BasePtr, b);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteSwap(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        byte b = (byte)instruction.B;
        (Reg(state.RegPtr, state.BasePtr, a), Reg(state.RegPtr, state.BasePtr, b)) = (
            Reg(state.RegPtr, state.BasePtr, b),
            Reg(state.RegPtr, state.BasePtr, a)
        );
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteUnm(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        Reg(state.RegPtr, state.BasePtr, a) = -valB;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteJump(Instruction instruction, ref VMState state)
    {
        state.Ip += instruction.sBx26 - 1;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ExecuteCallOrFFI(
        Instruction instruction,
        ref VMState state,
        VirtualMachine vm
    )
    {
        ushort methodIndex = instruction.B;
        if ((vm._methods[methodIndex] & 0x80000000) != 0)
        {
            byte start = instruction.A;
            state.BasePtr += start;
            state.RegPtr += state.BasePtr;
            vm._registeredHostMethods[methodIndex](ref state);
            state.RegPtr -= state.BasePtr;
            state.BasePtr -= start;
        }
        else
        {
            ExecuteCall(instruction, ref state);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteCall(Instruction instruction, ref VMState state)
    {
        if (state.CallStackPtr >= state.CallStackLimit)
        {
            throw new VMPanicException(
                VMStatus.StackOverflow,
                (int)(state.Ip - state.InstPtr - 1),
                "Call stack overflow"
            );
        }
        byte start = instruction.A;
        ushort methodIndex = instruction.B;
        int currentPcIndex = (int)(state.Ip - state.InstPtr);
        StackFrame frame = new StackFrame(currentPcIndex, state.BasePtr);
        CallStackPush(ref state.CallStackPtr, frame);
        state.BasePtr += start;
        state.RegPtr += state.BasePtr;

        state.Ip = state.InstPtr + (int)state.MethodTablePtr[methodIndex];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteReturn(Instruction instruction, ref VMState state)
    {
        byte start = instruction.A;
        byte end = (byte)instruction.B;
        byte count = (byte)(end - start);
        for (uint i = 0; i <= count; i++)
        {
            Reg(state.RegPtr, state.BasePtr, i) = Reg(state.RegPtr, state.BasePtr, start + i);
        }
        StackFrame frame = CallStackPop(ref state.CallStackPtr);
        int target = frame.ReturnPC;
        state.RegPtr -= state.BasePtr;
        state.BasePtr = frame.PreviousBase;
        state.Ip = state.InstPtr + target;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteAdd(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = valB + valC;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteSub(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = valB - valC;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteMul(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = valB * valC;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteDiv(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        if (valC == 0.0)
        {
            throw new VMPanicException(
                VMStatus.DivisionByZero,
                (int)(state.Ip - state.InstPtr - 1),
                "Division by zero"
            );
        }
        Reg(state.RegPtr, state.BasePtr, a) = valB / valC;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecutePow(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (float)Math.Pow(valB, valC);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteMod(Instruction instruction, ref VMState state) // TODO: Make sure that FISR works even with doubles
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = valB % valC;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteEq(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        bool comparison = valB == valC;
        bool expected = (a != 0);
        if (comparison == expected)
        {
            state.Ip++;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteLt(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        bool comparison = valB < valC;

        bool expected = (a != 0);
        if (comparison != expected)
        {
            state.Ip++;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteLe(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        bool comparison = valB <= valC;
        bool expected = (a != 0);
        if (comparison == expected)
        {
            state.Ip++;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void FlushOutput(ref VMState state)
    {
        if (state.OutBufferOffset > 0)
        {
            Console.Out.Write(new ReadOnlySpan<char>(state.OutBufferPtr, state.OutBufferOffset));
            state.OutBufferOffset = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecutePrint(Instruction instruction, ref VMState state)
    {
        uint b = (uint)instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];

        if (state.OutBufferCapacity - state.OutBufferOffset < 48)
        {
            FlushOutput(ref state);
        }

        Span<char> span = new Span<char>(
            state.OutBufferPtr + state.OutBufferOffset,
            state.OutBufferCapacity - state.OutBufferOffset
        );
        if (
            valB.TryFormat(
                span,
                out int charsWritten,
                default,
                System.Globalization.CultureInfo.InvariantCulture
            )
        )
        {
            state.OutBufferOffset += charsWritten;
            state.OutBufferPtr[state.OutBufferOffset++] = '\n';
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecutePrintA(Instruction instruction, ref VMState state)
    {
        uint b = (uint)instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];

        if (state.OutBufferCapacity - state.OutBufferOffset < 4)
        {
            FlushOutput(ref state);
        }

        state.OutBufferPtr[state.OutBufferOffset++] = (char)valB;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecutePrintS(Instruction instruction, ref VMState state)
    {
        uint b = (uint)instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];

        if (state.OutBufferCapacity - state.OutBufferOffset < 48)
        {
            FlushOutput(ref state);
        }

        Span<char> span = new Span<char>(
            state.OutBufferPtr + state.OutBufferOffset,
            state.OutBufferCapacity - state.OutBufferOffset
        );
        if (
            valB.TryFormat(
                span,
                out int charsWritten,
                default,
                System.Globalization.CultureInfo.InvariantCulture
            )
        )
        {
            state.OutBufferOffset += charsWritten;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteHalt(Instruction instruction, ref VMState state)
    {
        FlushOutput(ref state);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteRand(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        state.RngState ^= state.RngState << 13;
        state.RngState ^= state.RngState >> 17;
        state.RngState ^= state.RngState << 5;
        double result = state.RngState * 2.3283064365386963e-10;
        Reg(state.RegPtr, state.BasePtr, a) = result;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteSqrt(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (float)Math.Sqrt(valB);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteFisr(Instruction instruction, ref VMState state) // TODO: Make sure that FISR works even with doubles
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        long i;
        double x2,
            y;
        const float threehalfs = 1.5F;

        x2 = valB * 0.5d;
        y = valB;
        i = *(long*)&y; // evil floating point bit level hacking
        i = 0x5fe6eb50c7b537a9 - (i >> 1); // what the fuck?
        y = *(double*)&i;
        y = y * (threehalfs - (x2 * y * y)); // 1st iteration
        y = y * (threehalfs - (x2 * y * y)); // 2nd iteration, this can be removed
        Reg(state.RegPtr, state.BasePtr, a) = y;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteFor(Instruction instruction, ref VMState state)
    {
        byte index = instruction.A;
        ushort max = instruction.B;
        ushort step = instruction.C;

        double valIndex = Reg(state.RegPtr, state.BasePtr, index);
        double valMax =
            max < 256 ? Reg(state.RegPtr, state.BasePtr, max) : state.ConstPtr[max - 256];
        double valStep =
            step < 256 ? Reg(state.RegPtr, state.BasePtr, step) : state.ConstPtr[step - 256];
        valIndex += valStep;
        Reg(state.RegPtr, state.BasePtr, index) = valIndex;
        Instruction secondInst = new Instruction(*state.Ip++);
        byte condition = secondInst.A;
        bool conditionMet = false;
        switch (condition)
        {
            case 0:
                conditionMet = (valIndex < valMax);
                break;
            case 1:
                conditionMet = (valIndex > valMax);
                break;
            case 2:
                conditionMet = (valIndex <= valMax);
                break;
            case 3:
                conditionMet = (valIndex >= valMax);
                break;
        }
        if (conditionMet)
        {
            int jumpOffset = secondInst.sBx16;
            state.Ip += jumpOffset - 2;
        }
        return true;
    }

    /*
     * Thoughts and prayers
     * 1 free block and zero allocated arrays: done
     *
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteNewArray(Instruction instruction, ref VMState state)
    {
        byte pointerAddress = instruction.A;
        uint size = instruction.Bx;

        uint valSize = (uint)(
            size < 256 ? Reg(state.RegPtr, state.BasePtr, size) : state.ConstPtr[size - 256]
        );
        uint prevAddress = 0xFFFFFFFF;
        uint currAddress = 0xFFFFFFFF;
        uint nextAddress = state.FreeBlockHeaderPointer;
        uint blockSize = 0;
        while (nextAddress != 0xFFFFFFFF)
        {
            prevAddress = currAddress;
            currAddress = nextAddress;
            nextAddress = *(uint*)(state.HeapPtr + nextAddress);
            blockSize = *(uint*)(state.HeapPtr + currAddress + 4);
            if (blockSize >= valSize)
                break;
        }
        if (currAddress == 0xFFFFFFFF || blockSize < valSize)
        {
            throw new VMPanicException(
                VMStatus.OutOfMemory,
                (int)(state.Ip - state.InstPtr - 1),
                "VM heap ran out of memory"
            );
        }

        if (blockSize > valSize)
        {
            if (prevAddress != 0xFFFFFFFF)
                *(uint*)(state.HeapPtr + prevAddress) = nextAddress;
            else
            {
                state.FreeBlockHeaderPointer = currAddress + valSize + 4;
            }

            *(uint*)(state.HeapPtr + currAddress) = valSize;
            *(uint*)(state.HeapPtr + currAddress + valSize) = nextAddress;
            *(uint*)(state.HeapPtr + currAddress + valSize + 4) = blockSize - valSize;
            Reg(state.RegPtr, state.BasePtr, pointerAddress) = (double)
                (ulong)(state.HeapPtr + currAddress + 4);
        }
        else
        {
            if (prevAddress != 0xFFFFFFFF)
                *(uint*)(state.HeapPtr + prevAddress) = nextAddress;
            else
                state.FreeBlockHeaderPointer = 0xFFFFFFFF;
            *(uint*)(state.HeapPtr + currAddress) = valSize;
            Reg(state.RegPtr, state.BasePtr, pointerAddress) = (double)
                (ulong)(state.HeapPtr + currAddress + 4);
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteFreeArray(Instruction instruction, ref VMState state)
    {
        byte registerAddress = instruction.A;
        byte* arrayPtr = (byte*)(ulong)Reg(state.RegPtr, state.BasePtr, registerAddress);
        if (arrayPtr == null)
            return true;
        uint realAddress = (uint)(arrayPtr - state.HeapPtr) - 4;
        uint freedSize = *(uint*)(state.HeapPtr + realAddress);
        uint leftBlock = 0xFFFFFFFF;
        uint rightBlock = state.FreeBlockHeaderPointer;
        while (rightBlock != 0xFFFFFFFF && rightBlock < realAddress)
        {
            leftBlock = rightBlock;
            rightBlock = *(uint*)(state.HeapPtr + rightBlock); // Read the Next pointer
        }
        *(uint*)(state.HeapPtr + realAddress) = rightBlock;
        *(uint*)(state.HeapPtr + realAddress + 4) = freedSize;
        if (leftBlock == 0xFFFFFFFF)
            state.FreeBlockHeaderPointer = realAddress;
        else
            *(uint*)(state.HeapPtr + leftBlock) = realAddress;

        if (rightBlock != 0xFFFFFFFF && realAddress + freedSize == rightBlock)
        {
            uint rightSize = *(uint*)(state.HeapPtr + rightBlock + 4);

            freedSize += rightSize;
            *(uint*)(state.HeapPtr + realAddress + 4) = freedSize;

            uint blockAfterRight = *(uint*)(state.HeapPtr + rightBlock);
            *(uint*)(state.HeapPtr + realAddress) = blockAfterRight;

            rightBlock = blockAfterRight;
        }
        if (leftBlock != 0xFFFFFFFF)
        {
            uint leftSize = *(uint*)(state.HeapPtr + leftBlock + 4);

            if (leftBlock + leftSize == realAddress)
            {
                *(uint*)(state.HeapPtr + leftBlock + 4) = leftSize + freedSize;

                uint ourNext = *(uint*)(state.HeapPtr + realAddress);
                *(uint*)(state.HeapPtr + leftBlock) = ourNext;
            }
        }
        Reg(state.RegPtr, state.BasePtr, registerAddress) = 0;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteSetArray(Instruction instruction, ref VMState state)
    {
        byte pointerAddress = instruction.A;
        ushort index = instruction.B;
        ushort value = instruction.C;
        uint valIndex = (uint)(
            index < 256 ? Reg(state.RegPtr, state.BasePtr, index) : state.ConstPtr[index - 256]
        );
        double valValue =
            value < 256 ? Reg(state.RegPtr, state.BasePtr, value) : state.ConstPtr[value - 256];
        byte* destinationPtr = (byte*)(ulong)Reg(state.RegPtr, state.BasePtr, pointerAddress);
        *(double*)(destinationPtr + (valIndex) * 8) = valValue;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteSetArrayASCII(Instruction instruction, ref VMState state)
    {
        byte pointerAddress = instruction.A;
        ushort index = instruction.B;
        ushort value = instruction.C;
        uint valIndex = (uint)(
            index < 256 ? Reg(state.RegPtr, state.BasePtr, index) : state.ConstPtr[index - 256]
        );
        byte valValue = (byte)(
            value < 256 ? Reg(state.RegPtr, state.BasePtr, value) : state.ConstPtr[value - 256]
        );
        byte* destinationPtr = (byte*)(ulong)Reg(state.RegPtr, state.BasePtr, pointerAddress);
        *(destinationPtr + valIndex) = valValue;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteGetArray(Instruction instruction, ref VMState state)
    {
        byte destination = instruction.A;
        ushort register = instruction.B;
        byte* addressPtr = (byte*)(ulong)Reg(state.RegPtr, state.BasePtr, register);
        ushort c = instruction.C;
        uint valIndex = (uint)(
            c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256]
        );

        Reg(state.RegPtr, state.BasePtr, destination) = *(double*)(addressPtr + (valIndex * 8));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteGetArrayASCII(Instruction instruction, ref VMState state)
    {
        byte destination = instruction.A;
        ushort register = instruction.B;
        byte* addressPtr = (byte*)(ulong)Reg(state.RegPtr, state.BasePtr, register);
        ushort c = instruction.C;
        uint valIndex = (uint)(
            c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256]
        );

        Reg(state.RegPtr, state.BasePtr, destination) = *(addressPtr + valIndex);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteBinaryAnd(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (double)((long)valB & (long)valC);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteBinaryOr(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (double)((long)valB | (long)valC);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteBinaryXor(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (double)((long)valB ^ (long)valC);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteBinaryLeftShift(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (double)((long)valB << (int)valC);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool ExecuteBinaryRightShift(Instruction instruction, ref VMState state)
    {
        byte a = instruction.A;
        ushort b = instruction.B;
        double valB = b < 256 ? Reg(state.RegPtr, state.BasePtr, b) : state.ConstPtr[b - 256];
        ushort c = instruction.C;
        double valC = c < 256 ? Reg(state.RegPtr, state.BasePtr, c) : state.ConstPtr[c - 256];
        Reg(state.RegPtr, state.BasePtr, a) = (double)((long)valB >> (int)valC);
        return true;
    }

    public static string Disassemble(VMChunk chunk)
    {
        if (chunk == null || chunk.Instructions == null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        int pc = 0;
        uint[] instructions = chunk.Instructions;
        double[] constants = chunk.Constants;

        string GetValString(ushort val)
        {
            if (val < 256)
                return $"r{val}";
            int constIndex = val - 256;
            if (constIndex < constants.Length)
                return constants[constIndex]
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"c[{constIndex}]";
        }

        while (pc < instructions.Length)
        {
            Instruction inst = new Instruction(instructions[pc]);
            sb.Append($"{pc:D4}: ");

            switch (inst.Op)
            {
                case OpCode.LOADC:
                    {
                        uint bx = inst.Bx;
                        string val =
                            bx < constants.Length
                                ? constants[bx]
                                    .ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : $"c[{bx}]";
                        sb.AppendLine($"LOADC r{inst.A} {val}");
                    }
                    break;
                case OpCode.MOVE:
                    sb.AppendLine($"MOVE r{inst.A} r{inst.Bx}");
                    break;
                case OpCode.SWAP:
                    sb.AppendLine($"SWP r{inst.A} r{inst.Bx}");
                    break;
                case OpCode.ADD:
                case OpCode.SUB:
                case OpCode.MUL:
                case OpCode.DIV:
                case OpCode.POW:
                case OpCode.MOD:
                case OpCode.SETARR:
                case OpCode.SETARRA:
                case OpCode.GETARR:
                case OpCode.GETARRA:
                case OpCode.BINAND:
                case OpCode.BINOR:
                case OpCode.BINXOR:
                case OpCode.BINLSH:
                case OpCode.BINRSH:
                    {
                        string opName = inst.Op.ToString();
                        if (opName == "SWAP")
                            opName = "SWP";
                        sb.AppendLine(
                            $"{opName} r{inst.A} {GetValString(inst.B)} {GetValString(inst.C)}"
                        );
                    }
                    break;
                case OpCode.UNM:
                    sb.AppendLine($"UNM r{inst.A} {GetValString(inst.B)}");
                    break;
                case OpCode.JUMP:
                    {
                        int target = pc + 1 + inst.sBx26;
                        sb.AppendLine($"JUMP {target:D4}");
                    }
                    break;
                case OpCode.EQ:
                case OpCode.LT:
                case OpCode.LE:
                    sb.AppendLine(
                        $"{inst.Op} {inst.A} {GetValString(inst.B)} {GetValString(inst.C)}"
                    );
                    break;
                case OpCode.HALT:
                    sb.AppendLine("HALT");
                    break;
                case OpCode.PRINT:
                    sb.AppendLine($"PRINT {GetValString(inst.B)}");
                    break;
                case OpCode.PRINTA:
                    sb.AppendLine($"PRINTA {GetValString(inst.B)}");
                    break;
                case OpCode.RAND:
                    sb.AppendLine($"RAND r{inst.A}");
                    break;
                case OpCode.SQRT:
                    sb.AppendLine($"SQRT r{inst.A} {GetValString(inst.B)}");
                    break;
                case OpCode.FISR:
                    sb.AppendLine($"FISR r{inst.A} {GetValString(inst.B)}");
                    break;
                case OpCode.CALL:
                    {
                        uint methodIndex = inst.Bx;
                        sb.AppendLine($"CALL {methodIndex} r{inst.A}");
                    }
                    break;
                case OpCode.RETURN:
                    sb.AppendLine($"RETURN r{inst.A} r{inst.Bx}");
                    break;
                case OpCode.FOR:
                    {
                        if (pc + 1 < instructions.Length)
                        {
                            Instruction nextInst = new Instruction(instructions[pc + 1]);
                            if (nextInst.Op == OpCode.FOR)
                            {
                                byte rIndex = inst.A;
                                string rMax = GetValString(inst.B);
                                string rStep = GetValString(inst.C);
                                byte comp = nextInst.A;
                                string compStr = comp switch
                                {
                                    0 => "<",
                                    1 => ">",
                                    2 => "<=",
                                    3 => ">=",
                                    _ => "?",
                                };
                                int target = (pc + 1) + nextInst.sBx16;
                                sb.AppendLine(
                                    $"FOR r{rIndex} {rMax} {rStep} {compStr} {target:D4}"
                                );
                                pc++;
                                break;
                            }
                        }
                        sb.AppendLine(
                            $"FOR (incomplete) r{inst.A} {GetValString(inst.B)} {GetValString(inst.C)}"
                        );
                    }
                    break;
                case OpCode.FREEARR:
                    sb.AppendLine($"FREEARR r{inst.A}");
                    break;
                default:
                    sb.AppendLine($"UNKNOWN opcode {inst.Op} (value: {inst.Value})");
                    break;
            }
            pc++;
        }

        return sb.ToString();
    }

    public static string Disassemble(byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length < 4)
            return string.Empty;

        int instructionCount = bytecode.Length / 4;
        uint[] insts = new uint[instructionCount];
        for (int i = 0; i < instructionCount; i++)
        {
            insts[i] = BitConverter.ToUInt32(bytecode, i * 4);
        }

        VMChunk chunk = new VMChunk();
        chunk.Instructions = insts;
        return Disassemble(chunk);
    }
}
