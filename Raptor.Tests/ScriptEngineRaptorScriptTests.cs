using System;
using System.IO;
using Raptor;
using Raptor.StdLib;
using Xunit;

namespace Raptor.Tests
{
    public class ScriptEngineRaptorScriptTests
    {
        [Fact]
        public void ScriptEngineCompileRaptorScriptTest()
        {
            using var engine = new ScriptEngine();
            var table = new FFIHostTable();
            table.RegisterModule(typeof(RaptorMath));
            engine.RegisterHostTable(table);

            string raptorScript = @"
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
            string raptorScript = @"
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

            // High-level RaptorScript auto-detection via engine.Compile
            string raptorScript = "var x = 42;";
            VMChunk scriptChunk = engine.Compile(raptorScript);
            Assert.NotNull(scriptChunk);
            ExecutionResult scriptResult = engine.Execute(scriptChunk);
            Assert.Equal(VMStatus.Halted, scriptResult.Status);

            // Assembly auto-detection via engine.Compile
            string rasmScript = @"
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

            string script = @"
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

            // pow(3.0, 7.0) = 2187.0
            // min(3.0, 7.0) = 3.0
            // max(3.0, 7.0) = 7.0
            // clamp(7.0, 3.0, 10.0) = 7.0
            // pow(min(1, 2), max(3, 4)) = pow(1.0, 4.0) = 1.0
            Assert.Equal(2187.0, result.RegistersSnapshot[variables["powResult"]]);
            Assert.Equal(3.0, result.RegistersSnapshot[variables["minResult"]]);
            Assert.Equal(7.0, result.RegistersSnapshot[variables["maxResult"]]);
            Assert.Equal(7.0, result.RegistersSnapshot[variables["clampResult"]]);
            Assert.Equal(1.0, result.RegistersSnapshot[variables["nestedResult"]]);
        }
    }
}
