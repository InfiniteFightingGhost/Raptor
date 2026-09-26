using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

/// <summary>
/// Sweeps allocation size to expose the O(n) first-fit free-list behavior and
/// fragmentation under churn. <see cref="Size"/> scales the array payload; the
/// loop count is fixed so per-operation cost dominates.
/// </summary>
[MemoryDiagnoser]
public class AllocatorBenchmark
{
    [Params(1, 4, 16, 64, 256, 1024)]
    public int Size { get; set; }

    private VirtualMachine _vm = null!;
    private VMChunk _allocChunk = null!;
    private VMChunk _churnChunk = null!;

    [GlobalSetup]
    public void Setup()
    {
        _vm = new VirtualMachine();
        var engine = new ScriptEngine();

        _allocChunk = engine.Compile(
            $@"
DEFINE epochs 1000
DEFINE i r5
DEFINE size {Size}
DEFINE arr r1
LOADC i 0
loop:
    NEWARR arr size
    FREEARR arr
    FOR i epochs 1 < loop
HALT"
        );

        _churnChunk = engine.Compile(
            $@"
DEFINE epochs 400
DEFINE i r10
DEFINE size {Size}
DEFINE a r1
DEFINE b r2
DEFINE c r3
DEFINE d r4
LOADC i 0
loop:
    NEWARR a size
    NEWARR b size
    NEWARR c size
    NEWARR d size
    FREEARR b
    FREEARR d
    FREEARR a
    FREEARR c
    FOR i epochs 1 < loop
HALT"
        );
    }

    [Benchmark(Baseline = true)]
    public void AllocFree()
    {
        _vm.LoadProgram(_allocChunk);
        _vm.RunFast();
    }

    [Benchmark]
    public void AllocFreeChurn()
    {
        _vm.LoadProgram(_churnChunk);
        _vm.RunFast();
    }
}
