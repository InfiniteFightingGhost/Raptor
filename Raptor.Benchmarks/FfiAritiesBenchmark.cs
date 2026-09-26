using System.Text;
using BenchmarkDotNet.Attributes;
using Raptor;
using Raptor.Attributes;

namespace Raptor.Benchmarks;

/// <summary>
/// Exposes the FFI arity cliff: the typed wrapper has a zero-allocation fast path
/// for 0-4 double parameters, beyond which it falls back to the reflection wrapper
/// (ArrayPool + boxing, ~144 bytes/call). Same call, same loop, only arity changes.
/// </summary>
[MemoryDiagnoser]
public class FfiAritiesBenchmark
{
    private const int Epochs = 10_000;

    [Params(0, 1, 2, 3, 4, 5, 6, 8)]
    public int Arity { get; set; }

    private VirtualMachine _vm = null!;
    private VMChunk _chunk = null!;

    [GlobalSetup]
    public void Setup()
    {
        var table = new FFIHostTable();
        table.RegisterModule(typeof(FfiAritiesModule));

        _vm = new VirtualMachine();
        _vm.RegisterHostTable(table);

        var engine = new ScriptEngine();
        engine.RegisterHostTable(table);

        var sb = new StringBuilder();
        sb.AppendLine($"DEFINE epochs {Epochs}");
        sb.AppendLine("DEFINE i r15");
        for (int i = 0; i < Arity; i++)
        {
            sb.AppendLine($"LOADC r{i + 1} {i + 1}.0");
        }
        sb.AppendLine("LOADC i 0");
        sb.AppendLine("loop:");
        sb.AppendLine($"    CALL sum{Arity}() r1");
        sb.AppendLine("    FOR i epochs 1 < loop");
        sb.AppendLine("HALT");

        _chunk = engine.Compile(sb.ToString());
    }

    [Benchmark]
    public void FfiCallByArity()
    {
        _vm.LoadProgram(_chunk);
        _vm.RunFast();
    }

    [RaptorModule]
    public static class FfiAritiesModule
    {
        [RaptorMethod("sum0", 300)]
        public static double Sum0() => 0.0;

        [RaptorMethod("sum1", 301)]
        public static double Sum1(double a) => a;

        [RaptorMethod("sum2", 302)]
        public static double Sum2(double a, double b) => a + b;

        [RaptorMethod("sum3", 303)]
        public static double Sum3(double a, double b, double c) => a + b + c;

        [RaptorMethod("sum4", 304)]
        public static double Sum4(double a, double b, double c, double d) => a + b + c + d;

        [RaptorMethod("sum5", 305)]
        public static double Sum5(double a, double b, double c, double d, double e) =>
            a + b + c + d + e;

        [RaptorMethod("sum6", 306)]
        public static double Sum6(double a, double b, double c, double d, double e, double f) =>
            a + b + c + d + e + f;

        [RaptorMethod("sum8", 308)]
        public static double Sum8(
            double a,
            double b,
            double c,
            double d,
            double e,
            double f,
            double g,
            double h
        ) => a + b + c + d + e + f + g + h;
    }
}
