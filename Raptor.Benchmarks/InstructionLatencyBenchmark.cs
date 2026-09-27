using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

[MemoryDiagnoser]
public class InstructionLatencyBenchmark
{
    private VirtualMachine _vm = null!;
    private VMChunk _baseline = null!;
    private VMChunk _add = null!;
    private VMChunk _sub = null!;
    private VMChunk _mul = null!;
    private VMChunk _div = null!;
    private VMChunk _sqrt = null!;
    private VMChunk _fisr = null!;
    private VMChunk _rand = null!;
    private VMChunk _loadc = null!;
    private VMChunk _move = null!;
    private VMChunk _jump = null!;

    private const int Epochs = 50000;

    [GlobalSetup]
    public void Setup()
    {
        // Redirect stdout/stderr to avoid benchmark pollution


        _vm = new VirtualMachine();
        var engine = new ScriptEngine();

        _baseline = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC i 0
                LOADC r200 epochs
            loop:
                FOR i r200 1 < loop
            HALT");

        _add = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 1.5
            LOADC r2 2.5
            LOADC i 0
                LOADC r201 epochs
            loop:
                ADD r3 r1 r2
                FOR i r201 1 < loop
            HALT");

        _sub = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 1.5
            LOADC r2 2.5
            LOADC i 0
                LOADC r202 epochs
            loop:
                SUB r3 r1 r2
                FOR i r202 1 < loop
            HALT");

        _mul = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 1.5
            LOADC r2 2.5
            LOADC i 0
                LOADC r203 epochs
            loop:
                MUL r3 r1 r2
                FOR i r203 1 < loop
            HALT");

        _div = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 10.0
            LOADC r2 2.0
            LOADC i 0
                LOADC r204 epochs
            loop:
                DIV r3 r1 r2
                FOR i r204 1 < loop
            HALT");

        _sqrt = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 16.0
            LOADC i 0
                LOADC r205 epochs
            loop:
                SQRT r3 r1
                FOR i r205 1 < loop
            HALT");

        _fisr = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r1 16.0
            LOADC i 0
                LOADC r206 epochs
            loop:
                FISR r3 r1
                FOR i r206 1 < loop
            HALT");

        _rand = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC i 0
                LOADC r207 epochs
            loop:
                RAND r1
                FOR i r207 1 < loop
            HALT");

        _loadc = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC i 0
                LOADC r208 epochs
            loop:
                LOADC r1 5.5
                FOR i r208 1 < loop
            HALT");

        _move = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC r2 7.7
            LOADC i 0
                LOADC r209 epochs
            loop:
                MOVE r1 r2
                FOR i r209 1 < loop
            HALT");

        _jump = engine.Compile($@"
            DEFINE epochs {Epochs}
            DEFINE i r5
            LOADC i 0
                LOADC r210 epochs
            loop:
                JUMP target
            target:
                FOR i r210 1 < loop
            HALT");

        // Warm up and pre-load baseline program so registers array pins correctly
        _vm.LoadProgram(_baseline);
    }

    [Benchmark(Baseline = true)]
    public void Benchmark_Baseline()
    {
        _vm.LoadProgram(_baseline);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Add()
    {
        _vm.LoadProgram(_add);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Sub()
    {
        _vm.LoadProgram(_sub);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Mul()
    {
        _vm.LoadProgram(_mul);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Div()
    {
        _vm.LoadProgram(_div);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Sqrt()
    {
        _vm.LoadProgram(_sqrt);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Fisr()
    {
        _vm.LoadProgram(_fisr);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Rand()
    {
        _vm.LoadProgram(_rand);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Loadc()
    {
        _vm.LoadProgram(_loadc);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Move()
    {
        _vm.LoadProgram(_move);
        _vm.RunFast();
    }

    [Benchmark]
    public void Benchmark_Jump()
    {
        _vm.LoadProgram(_jump);
        _vm.RunFast();
    }
}
