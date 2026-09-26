using System.Linq;
using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

/// <summary>
/// Like-for-like A/B for the fused conditional branch. Both workloads perform the
/// exact same 50,000-step Fibonacci loop; the only difference is that one spends
/// two dispatches per iteration (<c>LT</c> + <c>JUMP</c>) and the other spends one
/// (<c>JLT</c>).
/// </summary>
[MemoryDiagnoser]
public class FusedBranchBenchmark
{
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

    [GlobalSetup]
    public void Setup()
    {
        _legacyVm = Build(LoopHead + "    LT 0 counter n\n    JUMP loop\nHALT");
        _fusedVm = Build(LoopHead + "    JLT 0 counter n loop\nHALT");
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

    [Benchmark(Baseline = true)]
    public void Loop_LegacyCompareJump() => _legacyVm.RunFast();

    [Benchmark]
    public void Loop_FusedBranch() => _fusedVm.RunFast();
}
