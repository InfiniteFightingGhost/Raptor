using System;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

[MemoryDiagnoser]
public class MemoryBenchmark
{
    private VirtualMachine _vm = null!;
    private VMChunk _arrayAccessChunk = null!;
    private VMChunk _allocDeallocCleanChunk = null!;
    private VMChunk _allocDeallocChurnChunk = null!;

    [GlobalSetup]
    public void Setup()
    {


        _vm = new VirtualMachine();
        var engine = new ScriptEngine();

        // 1. Benchmark memory read/write access
        _arrayAccessChunk = engine.Compile(@"
            DEFINE size 1000
            DEFINE epochs 10
            DEFINE arr r1
            DEFINE i r2
            DEFINE val r3
            DEFINE outer r4
            LOADC r200 size
            NEWARR arr r200
            LOADC outer 0
                LOADC r202 epochs
            outer_loop:
                LOADC i 0
                    LOADC r201 size
                loop:
                    SETARR arr i i
                    GETARR val arr i
                    FOR i r201 1 < loop
                FOR outer r202 1 < outer_loop
            FREEARR arr
            HALT");

        // 2. Best-case allocation/deallocation (constant sizing)
        _allocDeallocCleanChunk = engine.Compile(@"
            DEFINE epochs 1000
            DEFINE i r2
            DEFINE arr r1
            LOADC i 0
                LOADC r204 epochs
            loop:
                LOADC r203 32
                NEWARR arr r203
                FREEARR arr
                FOR i r204 1 < loop
            HALT");

        // 3. Out-of-order churn (variable sizing, creating free list traversal overhead)
        _allocDeallocChurnChunk = engine.Compile(@"
            DEFINE epochs 300
            DEFINE i r5
            DEFINE arr1 r1
            DEFINE arr2 r2
            DEFINE arr3 r3
            DEFINE arr4 r4
            LOADC i 0
                LOADC r209 epochs
            loop:
                LOADC r205 16
                NEWARR arr1 r205
                LOADC r206 32
                NEWARR arr2 r206
                LOADC r207 48
                NEWARR arr3 r207
                LOADC r208 64
                NEWARR arr4 r208
                
                ; Free in fragmented order to populate free list
                FREEARR arr2
                FREEARR arr4
                FREEARR arr1
                FREEARR arr3
                
                FOR i r209 1 < loop
            HALT");
    }

    [Benchmark(Baseline = true)]
    public void Memory_ArrayAccess()
    {
        _vm.LoadProgram(_arrayAccessChunk);
        _vm.RunFast();
    }

    [Benchmark]
    public void Memory_AllocDeallocClean()
    {
        _vm.LoadProgram(_allocDeallocCleanChunk);
        _vm.RunFast();
    }

    [Benchmark]
    public void Memory_AllocDeallocChurn()
    {
        _vm.LoadProgram(_allocDeallocChurnChunk);
        _vm.RunFast();
    }
}
