using System;
using Raptor;
using Raptor.Compiler;
using Xunit;

namespace Raptor.Tests
{
    public class EmitterEdgeCaseTests
    {
        [Fact]
        public void TestUnaryNotSelfAssignment()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 0;
                x = !x;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["x"]]);

            // Test toggling again back to 0
            string script2 = @"
                var x = 0;
                x = !x;
                x = !x;
            ";

            rasm = RaptorScriptCompiler.Compile(script2, out vars, new DiagnosticReporter());
            chunk = engine.Compile(rasm);
            result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(0.0, result.RegistersSnapshot[vars["x"]]);
        }

        [Fact]
        public void TestUnaryMinusSelfAssignment()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 42;
                x = -x;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(-42.0, result.RegistersSnapshot[vars["x"]]);
        }

        [Fact]
        public void TestComparisonSelfAssignments()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 5;
                x = x < 10;
                var y = 5;
                y = y > 10;
                var z = 5;
                z = z == 5;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["x"]]);
            Assert.Equal(0.0, result.RegistersSnapshot[vars["y"]]);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["z"]]);
        }

        [Fact]
        public void TestLogicalOpSelfAssignments()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = 0;
                a = a || 42;
                var b = 1;
                b = b && 100;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(42.0, result.RegistersSnapshot[vars["a"]]);
            Assert.Equal(100.0, result.RegistersSnapshot[vars["b"]]);
        }

        [Fact]
        public void TestIndexAccessSelfAssignment()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var arr = [10, 20];
                var i = 0;
                i = arr[i];
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(10.0, result.RegistersSnapshot[vars["i"]]);
        }

        [Fact]
        public void TestForLoopBodyEvaluationInvariance()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var sum = 0;
                for (var i = 0; i < 3; i++) {
                    var step = i * 10 + 5;
                    sum = sum + step;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            // i=0 -> step=5, sum=5
            // i=1 -> step=15, sum=20
            // i=2 -> step=25, sum=45
            Assert.Equal(45.0, result.RegistersSnapshot[vars["sum"]]);
        }

        [Fact]
        public void TestForLoopDecrementing()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var sum = 0;
                for (var i = 3; i > 0; i = i - 1) {
                    sum = sum + i;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            // 3 + 2 + 1 = 6
            Assert.Equal(6.0, result.RegistersSnapshot[vars["sum"]]);
        }
    }
}
