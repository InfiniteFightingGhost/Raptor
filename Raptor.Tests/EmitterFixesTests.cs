using System;
using System.Text;
using Raptor;
using Raptor.Compiler;
using Raptor.StdLib;
using Xunit;

namespace Raptor.Tests
{
    public class EmitterFixesTests
    {
        [Fact]
        public void TestForLoopWithNotEqualsCondition()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var count = 0;
                for (var i = 5; i != 0; i = i - 1) {
                    count = count + 1;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(5.0, result.RegistersSnapshot[vars["count"]]);
        }

        [Fact]
        public void TestForLoopWithEqualsCondition()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var count = 0;
                for (var i = 0; i == 0; i = i + 1) {
                    count = count + 1;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["count"]]);
        }

        [Fact]
        public void TestRootLevelRegisterResetNoOverflow()
        {
            using var engine = new ScriptEngine();
            var table = new FFIHostTable();
            table.RegisterModule(typeof(RaptorPeripherals));
            engine.RegisterHostTable(table);

            var sb = new StringBuilder();
            for (int i = 0; i < 300; i++)
            {
                sb.AppendLine("peri.print(1);");
            }
            sb.AppendLine("var finalVar = 42;");

            VMChunk chunk = engine.CompileRaptorScript(sb.ToString());
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
        }

        [Fact]
        public void TestLogicalOpTargetRegOverwriting()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = 42;
                var b = 1;
                a = b && a;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(42.0, result.RegistersSnapshot[vars["a"]]);
        }

        [Fact]
        public void TestArrayLiteralTargetRegOverwriting()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = [10, 20];
                a = [a[0] + 100, 200];
                var val0 = a[0];
                var val1 = a[1];
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(110.0, result.RegistersSnapshot[vars["val0"]]);
            Assert.Equal(200.0, result.RegistersSnapshot[vars["val1"]]);
        }

        [Fact]
        public void TestCompoundIndexAssignmentSingleEvaluation()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = [10, 20];
                a[0] += 5;
                var res = a[0];
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(15.0, result.RegistersSnapshot[vars["res"]]);
        }

        [Fact]
        public void TestIfConditionWithExpressionRhs()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 1;
                var y = 5;
                var res = 0;
                if (x < y + 1) {
                    res = 42;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(42.0, result.RegistersSnapshot[vars["res"]]);
        }

        [Fact]
        public void TestForLoopWithExpressionLimit()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var sum = 0;
                var limit = 3;
                for (var i = 0; i < limit + 2; i = i + 1) {
                    var dummy = 10 + 20;
                    sum = sum + i;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(10.0, result.RegistersSnapshot[vars["sum"]]); // 0 + 1 + 2 + 3 + 4 = 10
        }

        [Fact]
        public void TestWhileConditionWithExpressionRhs()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var i = 0;
                var max = 5;
                while (i < max - 1) {
                    i = i + 1;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(4.0, result.RegistersSnapshot[vars["i"]]);
        }

        [Fact]
        public void TestComparisonBinaryOpTargetRegCollision()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 10;
                x = x < 5;
                var y = 10;
                y = y == 10;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(0.0, result.RegistersSnapshot[vars["x"]]);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["y"]]);
        }

        [Fact]
        public void TestRegisterOverflowLimitCheck()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 260; i++)
            {
                sb.AppendLine($"var a{i} = {i};");
            }

            var reporter = new DiagnosticReporter();
            Assert.Throws<CompileException>(() =>
            {
                RaptorScriptCompiler.Compile(sb.ToString(), out _, reporter);
            });
            Assert.Contains(reporter.Diagnostics, d => d.Code == "E0027");
        }

        [Fact]
        public void TestDescendingForLoopRegisterScope()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var count = 0;
                for (var i = 10; i > 0; i = i - 1) {
                    var local = 42;
                    count = count + 1;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(10.0, result.RegistersSnapshot[vars["count"]]);
        }

        [Fact]
        public void TestNullArrayReferencePanic()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = 0;
                var val = a[0];
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.HostError, result.Status);
            Assert.Contains("Null reference exception", result.ErrorMessage);
        }

        [Fact]
        public void TestVarDeclInitializerEvaluationOrder()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = 100;
                var res = 0;
                var cond = 1;
                if (cond > 0) {
                    var a = a + 1;
                    res = a;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(101.0, result.RegistersSnapshot[vars["res"]]);
        }

        [Fact]
        public void TestForLoopWithoutIncrement()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var i = 0;
                var sum = 0;
                for (; i < 5;) {
                    sum = sum + i;
                    i = i + 1;
                }
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(10.0, result.RegistersSnapshot[vars["sum"]]);
            Assert.Equal(5.0, result.RegistersSnapshot[vars["i"]]);
        }

        [Fact]
        public void TestUnaryMinusExpression()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var x = 42;
                var negX = -x;
                var negLiteral = -100;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(-42.0, result.RegistersSnapshot[vars["negX"]]);
            Assert.Equal(-100.0, result.RegistersSnapshot[vars["negLiteral"]]);
        }

        [Fact]
        public void TestUnaryNotExpression()
        {
            using var engine = new ScriptEngine();
            string script = @"
                var a = 0;
                var notA = !a;
                var notNotA = !notA;
            ";

            string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
            VMChunk chunk = engine.Compile(rasm);
            ExecutionResult result = engine.Execute(chunk);

            Assert.Equal(VMStatus.Halted, result.Status);
            Assert.Equal(1.0, result.RegistersSnapshot[vars["notA"]]);
            Assert.Equal(0.0, result.RegistersSnapshot[vars["notNotA"]]);
        }
    }
}

