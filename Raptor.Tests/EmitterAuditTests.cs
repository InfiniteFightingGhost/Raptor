using Raptor;
using Raptor.Compiler;
using Xunit;

namespace Raptor.Tests
{
    public class EmitterAuditTests
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
            // i=0(0), i=1(1), i=2(3), i=3(6), i=4(10, flag=0). Next check fails because flag==0.
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
            Assert.Equal(6.0, result.RegistersSnapshot[vars["res1"]]); // 2 + 4 = 6
            Assert.Equal(1.0, result.RegistersSnapshot[vars["res2"]]); // 0 + 1 = 1
        }
    }
}
