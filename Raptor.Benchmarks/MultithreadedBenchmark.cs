using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

[MemoryDiagnoser]
public class MultithreadedBenchmark
{
    private VMChunk _physicsChunk = null!;
    private VirtualMachine[] _vms = null!;

    [GlobalSetup]
    public void Setup()
    {


        var engine = new ScriptEngine();

        const string PhysicsMovementAsm = @"
            DEFINE epochs 10000
            DEFINE i r8
            LOADC r1 0.0
            LOADC r2 10.0
            LOADC r3 2.5
            LOADC r4 0.0
            LOADC r5 9.81
            LOADC r6 0.016
            LOADC r7 0.0
            LOADC i 0
                LOADC r200 epochs
            loop:
                MUL r9 r5 r6
                SUB r4 r4 r9
                MUL r10 r3 r6
                ADD r1 r1 r10
                MUL r11 r4 r6
                ADD r2 r2 r11
                LT 0 r2 r7
                JUMP skip_ground
                MOVE r2 r7
                LOADC r4 0.0
            skip_ground:
                FOR i r200 1 < loop
            HALT";

        _physicsChunk = engine.Compile(PhysicsMovementAsm);

        // Pre-allocate 8 separate VM instances to run concurrently without state corruption
        _vms = new VirtualMachine[8];
        for (int i = 0; i < 8; i++)
        {
            _vms[i] = new VirtualMachine();
            _vms[i].LoadProgram(_physicsChunk);
        }
    }

    [Params(1, 2, 4, 8)]
    public int VmCount { get; set; }

    [Benchmark]
    public void MultiVmScaling()
    {
        if (VmCount == 1)
        {
            _vms[0].RunFast();
            return;
        }

        Parallel.For(0, VmCount, i =>
        {
            _vms[i].RunFast();
        });
    }
}
