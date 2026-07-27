using System;
using System.IO;
using Raptor;
using Raptor.StdLib;
using Xunit;

namespace Raptor.Tests;

public class ScriptEngineTests
{
    [Fact]
    public void ScriptEngineRunTest()
    {
        using ScriptEngine engine = new ScriptEngine();
        ExecutionResult runResult = engine.Run(
            @"
LOADC r1 7.0
LOADC r2 6.0
MUL r3 r1 r2
HALT
"
        );
        Assert.Equal(VMStatus.Halted, runResult.Status);
        Assert.Equal(42.0, runResult.RegistersSnapshot[3]);
    }

    [Fact]
    public void ScriptEngineExecuteFileTest()
    {
        using ScriptEngine engine = new ScriptEngine();
        string enginePath = Path.Combine(Path.GetTempPath(), "raptor_test_engine.rbc");
        try
        {
            VMChunk engineChunk = engine.Compile(
                @"
LOADC r1 123.0
LOADC r2 456.0
ADD r3 r1 r2
HALT
"
            );
            engine.SaveToFile(engineChunk, enginePath);
            ExecutionResult fileResult = engine.Execute(enginePath);
            Assert.Equal(VMStatus.Halted, fileResult.Status);
            Assert.Equal(579.0, fileResult.RegistersSnapshot[3]);
        }
        finally
        {
            if (File.Exists(enginePath))
                File.Delete(enginePath);
        }
    }

    [Fact]
    public void ScriptEngineAndFfiTest()
    {
        using ScriptEngine ffiEngine = new ScriptEngine();
        ffiEngine.RegisterHostMethod(
            "double",
            0,
            (ref VMState state) =>
            {
                unsafe
                {
                    state.RegPtr[0] = state.RegPtr[0] * 2.0;
                }
            }
        );
        ExecutionResult ffiResult = ffiEngine.Run(
            @"
LOADC r1 21.0
CALL double() r1
MOVE r2 r1
HALT
"
        );
        Assert.Equal(VMStatus.Halted, ffiResult.Status);
        Assert.Equal(42.0, ffiResult.RegistersSnapshot[2]);
    }

    [Fact]
    public void ScriptEngineCompileFileTest()
    {
        using ScriptEngine engine = new ScriptEngine();
        string rasmPath = Path.Combine(Path.GetTempPath(), "test_script.rasm");
        try
        {
            File.WriteAllText(
                rasmPath,
                @"
LOADC r1 10.0
LOADC r2 5.0
SUB r3 r1 r2
HALT
"
            );
            VMChunk chunk = engine.CompileFile(rasmPath);
            ExecutionResult result = engine.Execute(chunk);
            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(5.0, result.RegistersSnapshot[3]);
        }
        finally
        {
            if (File.Exists(rasmPath))
                File.Delete(rasmPath);
        }
    }

    [Fact]
    public void ScriptEngineRunFileTest()
    {
        using ScriptEngine engine = new ScriptEngine();
        string rasmPath = Path.Combine(Path.GetTempPath(), $"test_run_script_{Guid.NewGuid():N}.rasm");
        try
        {
            File.WriteAllText(
                rasmPath,
                @"
LOADC r1 8.0
LOADC r2 9.0
MUL r3 r1 r2
HALT
"
            );
            ExecutionResult result = engine.RunFile(rasmPath);
            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(72.0, result.RegistersSnapshot[3]);
        }
        finally
        {
            if (File.Exists(rasmPath))
                File.Delete(rasmPath);
        }
    }

    [Fact]
    public void ScriptWatcherHotReloadTest()
    {
        using ScriptEngine engine = new ScriptEngine();
        string rasmPath = Path.Combine(Path.GetTempPath(), $"test_hotreload_{Guid.NewGuid():N}.rasm");
        try
        {
            File.WriteAllText(
                rasmPath,
                @"
LOADC r1 10.0
HALT
"
            );
            using ScriptWatcher watcher = new ScriptWatcher(engine, rasmPath);

            ExecutionResult result1 = engine.Execute(watcher.ActiveChunk);
            Assert.Equal(10.0, result1.RegistersSnapshot[1]);

            bool reloadedFired = false;
            watcher.OnReloaded += (chunk) => reloadedFired = true;

            File.WriteAllText(
                rasmPath,
                @"
LOADC r1 20.0
HALT
"
            );

            for (int i = 0; i < 300; i++)
            {
                if (reloadedFired)
                    break;
                System.Threading.Thread.Sleep(10);
            }

            Assert.True(reloadedFired, "Reload event did not fire.");

            ExecutionResult result2 = engine.Execute(watcher.ActiveChunk);
            Assert.Equal(20.0, result2.RegistersSnapshot[1]);
        }
        finally
        {
            if (File.Exists(rasmPath))
                File.Delete(rasmPath);
        }
    }

    [Fact]
    public void SourceMap_TranslateError_MapsRuntimeExceptionsCorrectly()
    {
        string raptorScript =
            @"
var x = 10.0;
var y = 0.0;
var result = x / y; // Division by zero!
";
        string rasmCode = Raptor.Compiler.RaptorScriptCompiler.Compile(
            raptorScript,
            reporter: new Raptor.Compiler.DiagnosticReporter()
        );

        using ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);
        ExecutionResult runResult = engine.Execute(chunk);

        Assert.Equal(VMStatus.DivisionByZero, runResult.Status);

        string errorDetails = ScriptEngine.TranslateError(chunk, runResult.IpOffset, raptorScript);

        Assert.Contains("Runtime error at line 4", errorDetails);
        Assert.Contains("var result = x / y;", errorDetails);
    }

    [Fact]
    public void ScriptEngineCompileRaptorScriptTest()
    {
        using var engine = new ScriptEngine();
        var table = new FFIHostTable();
        table.RegisterModule(typeof(RaptorMath));
        engine.RegisterHostTable(table);

        string raptorScript =
            @"
            var radius = 5.0;
            var area = math.pi() * math.pow(radius, 2.0);
        ";

        VMChunk chunk = engine.CompileRaptorScript(raptorScript);
        Assert.NotNull(chunk);
        Assert.NotEmpty(chunk.Instructions);

        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
    }

    [Fact]
    public void ScriptEngineRunRaptorScriptTest()
    {
        using var engine = new ScriptEngine();
        string raptorScript =
            @"
            var x = 10;
            var y = 20;
            var z = x + y;
            ";

        ExecutionResult result = engine.RunRaptorScript(raptorScript);
        Assert.Equal(VMStatus.Halted, result.Status);
    }

    [Fact]
    public void ScriptEngineSmartCompileAutoDetectsRaptorScriptAndAssembly()
    {
        using var engine = new ScriptEngine();

        string raptorScript = "var x = 42;";
        VMChunk scriptChunk = engine.Compile(raptorScript);
        Assert.NotNull(scriptChunk);
        ExecutionResult scriptResult = engine.Execute(scriptChunk);
        Assert.Equal(VMStatus.Halted, scriptResult.Status);

        string rasmScript =
            @"
            LOADC r1 42.0
            HALT
        ";
        VMChunk rasmChunk = engine.Compile(rasmScript);
        Assert.NotNull(rasmChunk);
        ExecutionResult rasmResult = engine.Execute(rasmChunk);
        Assert.Equal(VMStatus.Halted, rasmResult.Status);
        Assert.Equal(42.0, rasmResult.RegistersSnapshot[1]);
    }

    [Fact]
    public void ScriptEngine_MethodCall_PreservesParameterValuesAndExpressions()
    {
        using var engine = new ScriptEngine();
        var table = new FFIHostTable();
        table.RegisterModule(typeof(RaptorMath));
        engine.RegisterHostTable(table);

        string script =
            @"
            var a = 1.0;
            var b = 2.0;
            var c = 3.0;
            var d = 4.0;
            var powResult = math.pow(a + b, c + d);
            var minResult = math.min(a + b, c + d);
            var maxResult = math.max(a + b, c + d);
            var clampResult = math.clamp(c + d, a + b, 10.0);
            var nestedResult = math.pow(math.min(a, b), math.max(c, d));
        ";

        string rasm = Raptor.Compiler.RaptorScriptCompiler.Compile(
            script,
            out var variables,
            new Raptor.Compiler.DiagnosticReporter()
        );

        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);

        Assert.Equal(2187.0, result.RegistersSnapshot[variables["powResult"]]);
        Assert.Equal(3.0, result.RegistersSnapshot[variables["minResult"]]);
        Assert.Equal(7.0, result.RegistersSnapshot[variables["maxResult"]]);
        Assert.Equal(7.0, result.RegistersSnapshot[variables["clampResult"]]);
        Assert.Equal(1.0, result.RegistersSnapshot[variables["nestedResult"]]);
    }
}
