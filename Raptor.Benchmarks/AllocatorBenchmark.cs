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
    LOADC r201 epochs
loop:
    LOADC r200 size
    NEWARR arr r200
    FREEARR arr
    FOR i r201 1 < loop
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
    LOADC r206 epochs
loop:
    LOADC r202 size
    NEWARR a r202
    LOADC r203 size
    NEWARR b r203
    LOADC r204 size
    NEWARR c r204
    LOADC r205 size
    NEWARR d r205
    FREEARR b
    FREEARR d
    FREEARR a
    FREEARR c
    FOR i r206 1 < loop
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
