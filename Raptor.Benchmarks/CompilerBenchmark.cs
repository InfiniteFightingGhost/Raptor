using System.IO;
using BenchmarkDotNet.Attributes;
using Raptor;
using Raptor.Compiler;

namespace Raptor.Benchmarks;

/// <summary>
/// Measures compiler pipeline throughput in isolation: RaptorScript -> assembly,
/// assembly -> verified chunk, verification, disassembly, and .rbc round trips.
/// This is the only suite that exercises the lexer/parser/AST-optimizer/emitter,
/// which the other benchmarks bypass entirely.
/// </summary>
[MemoryDiagnoser]
public class CompilerBenchmark
{
    private const int Epochs = 8;

    private const string RaptSource =
        @"
var sum = 0.0;
var arr = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0];
for (var i = 0.0; i < 8.0; i = i + 1.0) {
    if (i > 4.0) {
        sum = sum + arr[i] * 2.0;
    } else {
        sum = sum - arr[i];
    }
}
var count = 0.0;
while (sum < 100.0 && count < 8.0) {
    sum = sum + 1.5;
    count = count + 1.0;
}
sum = sum * 2.0 + 1.0 - 3.0 / 2.0;
";

    private const string AsmSource =
        @"
DEFINE epochs 1000
DEFINE i r1
LOADC i 0
loop:
    ADD r2 r2 r3
    MUL r4 r2 r3
    FOR i epochs 1 < loop
HALT";

    private ScriptEngine _engine = null!;
    private VMChunk _raptChunk = null!;
    private VMChunk _asmChunk = null!;
    private MemoryStream _saveStream = null!;
    private MemoryStream _loadStream = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new ScriptEngine();
        _raptChunk = _engine.Compile(RaptSource);
        _asmChunk = _engine.Compile(AsmSource);

        _saveStream = new MemoryStream();
        RaptorBinary.Save(_raptChunk, _saveStream);

        _loadStream = new MemoryStream(_saveStream.ToArray());
    }

    [Benchmark(Baseline = true)]
    public string RaptorScriptToAssembly() =>
        RaptorScriptCompiler.Compile(RaptSource, new DiagnosticReporter());

    [Benchmark]
    public VMChunk RaptorScriptToChunk() => _engine.Compile(RaptSource);

    [Benchmark]
    public VMChunk AssemblyToChunk() => _engine.Compile(AsmSource);

    [Benchmark]
    public void VerifyChunk() => BytecodeVerifier.Verify(_raptChunk, 512 * 1024);

    [Benchmark]
    public string DisassembleChunk() => Disassembler.Disassemble(_raptChunk);

    [Benchmark]
    public void BinarySave()
    {
        _saveStream.SetLength(0);
        RaptorBinary.Save(_raptChunk, _saveStream);
    }

    [Benchmark]
    public VMChunk BinaryLoad()
    {
        _loadStream.Position = 0;
        return RaptorBinary.Load(_loadStream);
    }
}
