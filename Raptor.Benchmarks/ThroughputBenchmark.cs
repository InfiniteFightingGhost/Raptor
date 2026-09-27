using System.Linq;
using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

/// <summary>
/// Interpreter throughput normalized to <b>VM instructions</b> rather than whole program runs.
/// Each <see cref="BenchmarkAttribute.OperationsPerInvoke"/> is set to the exact number of VM
/// instructions the program dispatches, so BenchmarkDotNet's Mean column becomes
/// <b>nanoseconds per VM instruction</b>. Derive throughput with:
/// <code>VM MIPS = 1000 / Mean(ns)</code>
/// The counts are asserted at setup time with <see cref="VirtualMachine.RunProfile"/> so the
/// constants can never silently drift out of sync with the assembler.
/// </summary>
[MemoryDiagnoser]
public class ThroughputBenchmark
{
    // Linear Fibonacci: counter increments 1..49999, then HALT. On the final iteration the
    // compare is false and skips its JUMP, so that iteration dispatches 5 not 6 instructions.
    private const int Iterations = 49999;
    private const int LegacyInstructions = 2 + (Iterations * 6 - 1) + 1; // LOADC,LOADC, loop, HALT
    private const int FusedInstructions = 2 + Iterations * 5 + 1; // JLT replaces LT + JUMP

    private VirtualMachine _legacyVm = null!;
    private VirtualMachine _fusedVm = null!;

    private const string LoopHead =
        @"
DEFINE result r0
DEFINE last r1
DEFINE lastlast r2
DEFINE counter r4
DEFINE n 50000
LOADC result 1
LOADC counter 1
loop:
    MOVE lastlast last
    MOVE last result
    ADD result last lastlast
    ADD counter counter 1
";

    private const string LegacyAsm = LoopHead + "    LT 0 counter n\n    JUMP loop\nHALT";
    private const string FusedAsm = LoopHead + "    JLT 0 counter n loop\nHALT";

    [GlobalSetup]
    public void Setup()
    {
        _legacyVm = Build(LegacyAsm);
        _fusedVm = Build(FusedAsm);
        AssertInstructionCount(LegacyAsm, LegacyInstructions);
        AssertInstructionCount(FusedAsm, FusedInstructions);
    }

    private static VirtualMachine Build(string asm)
    {
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split('\n').ToList());
        BytecodeVerifier.Verify(chunk, 16 * 1024 * 1024);
        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        return vm;
    }

    private static void AssertInstructionCount(string asm, int expected)
    {
        VirtualMachine vm = Build(asm);
        vm.RunProfile(new ulong[64], out ulong total);
        if (total != (ulong)expected)
        {
            throw new InvalidOperationException(
                $"VM instruction count drift: expected {expected}, RunProfile reported {total}."
            );
        }
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = LegacyInstructions)]
    public void Fibonacci_Legacy() => _legacyVm.RunFast();

    [Benchmark(OperationsPerInvoke = FusedInstructions)]
    public void Fibonacci_Fused() => _fusedVm.RunFast();
}