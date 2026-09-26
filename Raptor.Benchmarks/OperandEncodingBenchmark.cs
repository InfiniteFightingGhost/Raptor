using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

/// <summary>
/// Compares the Register/Constant (RC) operand paths on the same arithmetic loop:
/// register-only operands force the <c>&lt; 256</c> branch and pointer loads,
/// constant-only operands exercise the constant-pool load path, and mixed operands
/// measure the combination.
/// </summary>
[MemoryDiagnoser]
public class OperandEncodingBenchmark
{
    private VirtualMachine _vm = null!;
    private VMChunk _registerOnly = null!;
    private VMChunk _constantOnly = null!;
    private VMChunk _mixed = null!;

    [GlobalSetup]
    public void Setup()
    {
        _vm = new VirtualMachine();
        var engine = new ScriptEngine();

        _registerOnly = engine.Compile(
            @"
DEFINE epochs 50000
DEFINE i r5
LOADC r1 1.5
LOADC r2 2.5
LOADC i 0
loop:
    ADD r3 r1 r2
    MUL r4 r1 r3
    FOR i epochs 1 < loop
HALT"
        );

        _constantOnly = engine.Compile(
            @"
DEFINE epochs 50000
DEFINE i r5
LOADC i 0
loop:
    ADD r3 1.5 2.5
    MUL r4 1.5 r3
    FOR i epochs 1 < loop
HALT"
        );

        _mixed = engine.Compile(
            @"
DEFINE epochs 50000
DEFINE i r5
LOADC r1 1.5
LOADC i 0
loop:
    ADD r3 r1 2.5
    MUL r4 1.5 r3
    FOR i epochs 1 < loop
HALT"
        );
    }

    [Benchmark(Baseline = true)]
    public void RegisterOperands()
    {
        _vm.LoadProgram(_registerOnly);
        _vm.RunFast();
    }

    [Benchmark]
    public void ConstantOperands()
    {
        _vm.LoadProgram(_constantOnly);
        _vm.RunFast();
    }

    [Benchmark]
    public void MixedOperands()
    {
        _vm.LoadProgram(_mixed);
        _vm.RunFast();
    }
}
