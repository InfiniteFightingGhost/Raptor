using System;
using System.Text;
using Raptor;
using Raptor.Compiler;
using Raptor.StdLib;
using Xunit;

namespace Raptor.Tests;

public class EmitterTests
{
    [Fact]
    public void TestComplexForLoopConditionWithLogicalAnd()
    {
        string script = @"
var total = 0.0;
var flag = 1.0;
for (var i = 0.0; i < 10.0 && flag; i = i + 1.0) {
    total = total + i;
    if (i == 4.0) {
        flag = 0.0;
    }
}
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(10.0, result.RegistersSnapshot[vars["total"]]);
    }

    [Fact]
    public void TestForLoopWithFunctionCallCondition()
    {
        string script = @"
var arr = [10.0, 20.0, 30.0];
var sum = 0.0;
for (var i = 0.0; i < len(arr); i = i + 1.0) {
    sum = sum + arr[i];
}
free(arr);
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(60.0, result.RegistersSnapshot[vars["sum"]]);
    }

    [Fact]
    public void TestNestedIfAndLoopScopeRegisterPreservation()
    {
        string script = @"
var a = 100.0;
if (a > 50.0) {
    var b = 200.0;
    if (b > 100.0) {
        var c = 300.0;
        var temp = b + c;
    }
    var d = 400.0;
}
var e = 500.0;
var result = a + e;
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(100.0, result.RegistersSnapshot[vars["a"]]);
        Assert.Equal(500.0, result.RegistersSnapshot[vars["e"]]);
        Assert.Equal(600.0, result.RegistersSnapshot[vars["result"]]);
    }

    [Fact]
    public void TestShortCircuitLogicalOperations()
    {
        string script = @"
var a = 1.0;
var b = 2.0;
var c = 0.0;
var d = 4.0;

var res1 = (a && b) + (c || d);
var res2 = (c && b) + (a || d);
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(6.0, result.RegistersSnapshot[vars["res1"]]);
        Assert.Equal(1.0, result.RegistersSnapshot[vars["res2"]]);
    }

    [Fact]
    public void TestRegisterScopingDoesNotExposeSyntheticVariablesInGlobals()
    {
        string script = @"
for (var i = 0; i < 5; i = i + 1) {
    var x = i * 2;
}
";
        var reporter = new DiagnosticReporter();
        var parser = new Parser(new Lexer(script, reporter).ScanTokens(), reporter);
        var ast = parser.Parse();
        var emitter = new Emitter(ast, reporter);
        emitter.Emit();

        Assert.False(reporter.HasErrors);
        foreach (var key in emitter.Globals.Keys)
        {
            Assert.DoesNotContain("__for_", key);
        }
    }

    [Fact]
    public void TestCallParameterRegistersReclaimedImmediately()
    {
        string script = @"
var arr = [10.0, 20.0, 30.0];
var l = len(arr);
var b = 42.0;
free(arr);
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(3.0, result.RegistersSnapshot[vars["l"]]);
        Assert.Equal(42.0, result.RegistersSnapshot[vars["b"]]);
    }

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
        Assert.Equal(6.0, result.RegistersSnapshot[vars["sum"]]);
    }

    [Fact]
    public void TestSpacedDotMemberAndMethodAccess()
    {
        using var engine = new ScriptEngine();
        var table = new FFIHostTable();
        table.RegisterModule(typeof(RaptorPeripherals));
        engine.RegisterHostTable(table);

        string script = @"
            var val = 100.0;
            peri . print ( val ) ;
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(100.0, result.RegistersSnapshot[vars["val"]]);
    }

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
        Assert.Equal(10.0, result.RegistersSnapshot[vars["sum"]]);
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

    [Fact]
    public void TestGreaterThanBranchConditionsWithConstants()
    {
        using var engine = new ScriptEngine();
        string script = @"
            var x = 10;
            var res1 = 0;
            var res2 = 0;
            if (x > 5) {
                res1 = 1;
            }
            if (x >= 10) {
                res2 = 1;
            }
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(1.0, result.RegistersSnapshot[vars["res1"]]);
        Assert.Equal(1.0, result.RegistersSnapshot[vars["res2"]]);
    }

    [Fact]
    public void TestNestedBlockNoVariablesRegisterInvariants()
    {
        using var engine = new ScriptEngine();
        string script = @"
            var a = 100;
            if (a == 100) {
            }
            var b = a + 50;
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(100.0, result.RegistersSnapshot[vars["a"]]);
        Assert.Equal(150.0, result.RegistersSnapshot[vars["b"]]);
    }

    [Fact]
    public void TestForLoopMultiStatementJumpTarget()
    {
        using var engine = new ScriptEngine();
        string script = @"
            var sum = 0;
            for (var j = 0; j < 3; j = j + 1) {
                var add = 10;
                add = add + j;
                sum = sum + add;
            }
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(33.0, result.RegistersSnapshot[vars["sum"]]);
    }

    [Fact]
    public void TestEqualityAndRelationalOperatorPrecedence()
    {
        using var engine = new ScriptEngine();
        string script = @"
            var res = (2 < 5 == 4 < 10);
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(1.0, result.RegistersSnapshot[vars["res"]]);
    }

    [Fact]
    public void TestOptimizerNonZeroTruthyCondition()
    {
        using var engine = new ScriptEngine();
        string script = @"
            var res = 0;
            if (42) {
                res = 100;
            }
        ";

        string rasm = RaptorScriptCompiler.Compile(script, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(100.0, result.RegistersSnapshot[vars["res"]]);
    }
}
