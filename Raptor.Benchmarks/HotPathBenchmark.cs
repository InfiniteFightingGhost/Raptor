using BenchmarkDotNet.Attributes;
using Raptor;

namespace Raptor.Benchmarks;

/// <summary>
/// Isolates the interpreter hot loop from <c>LoadProgram</c> (GCHandle pinning,
/// register clearing, host-table stamping). Each chunk is compiled and loaded once
/// in setup; the benchmark measures only <c>RunFast()</c>. Compare against
/// <see cref="VmBenchmarks"/>, which includes load cost inside the measurement.
/// </summary>
[MemoryDiagnoser]
public class HotPathBenchmark
{
    private VirtualMachine _fibVm = null!;
    private VirtualMachine _monteCarloVm = null!;
    private VirtualMachine _physicsVm = null!;
    private VirtualMachine _ecsVm = null!;

    [GlobalSetup]
    public void Setup()
    {
        var engine = new ScriptEngine();
        _fibVm = Warm(engine, LinearFibAsm);
        _monteCarloVm = Warm(engine, MonteCarloAsm);
        _physicsVm = Warm(engine, PhysicsAsm);
        _ecsVm = Warm(engine, EcsAsm);
    }

    private static VirtualMachine Warm(ScriptEngine engine, string asm)
    {
        var chunk = engine.Compile(asm);
        var vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        return vm;
    }

    [Benchmark(Baseline = true)]
    public void RunFast_Fibonacci() => _fibVm.RunFast();

    [Benchmark]
    public void RunFast_MonteCarlo() => _monteCarloVm.RunFast();

    [Benchmark]
    public void RunFast_Physics() => _physicsVm.RunFast();

    [Benchmark]
    public void RunFast_Ecs() => _ecsVm.RunFast();

    private const string LinearFibAsm =
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
    LT 0 counter n
    JUMP loop
HALT";

    private const string MonteCarloAsm =
        @"
DEFINE epochs 100000
DEFINE x r1
DEFINE y r2
DEFINE hits r4
DEFINE i r5
LOADC hits 0
LOADC i 0
loop:
    RAND x
    RAND y
    MUL x x x
    MUL y y y
    ADD y y x
    LE 0 y 1
    ADD hits hits 1
    FOR i epochs 1 < loop
HALT";

    private const string PhysicsAsm =
        @"
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
loop:
    MUL r9 r5 r6
    SUB r4 r4 r9
    MUL r10 r3 r6
    ADD r1 r1 r10
    MUL r11 r4 r6
    ADD r2 r2 r11
    LT 1 r2 r7
    JUMP skip_ground
    MOVE r2 r7
    LOADC r4 0.0
skip_ground:
    FOR i epochs 1 < loop
HALT";

    private const string EcsAsm =
        @"
DEFINE size 1000
DEFINE dt 0.016
DEFINE pos_arr r1
DEFINE vel_arr r2
DEFINE i r3
DEFINE px r4
DEFINE py r5
DEFINE vx r6
DEFINE vy r7
DEFINE temp r8
NEWARR pos_arr size
NEWARR vel_arr size
LOADC i 0
loop:
    GETARR px pos_arr i
    GETARR vx vel_arr i
    MUL temp vx dt
    ADD px px temp
    SETARR pos_arr i px
    GETARR py pos_arr i
    GETARR vy vel_arr i
    MUL temp vy dt
    ADD py py temp
    SETARR pos_arr i py
    FOR i size 1 < loop
FREEARR pos_arr
FREEARR vel_arr
HALT";
}
