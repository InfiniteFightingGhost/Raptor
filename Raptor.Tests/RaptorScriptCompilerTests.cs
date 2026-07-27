using System;
using System.Collections.Generic;
using System.Linq;
using Raptor;
using Raptor.Compiler;
using Xunit;

namespace Raptor.Tests;

public class RaptorScriptCompilerTests
{
    [Fact]
    public void TestLexerTokenStartColumnTracking()
    {
        var reporter = new DiagnosticReporter();
        var lexer = new Lexer("var x = 10 != 20 <= 30 >= 40;", reporter);
        List<Token> tokens = lexer.ScanTokens();

        Assert.Equal(1, tokens[0].Column);
        Token notEqualToken = tokens.Find(t => t.Type == TokenType.NotEqual)!;
        Assert.NotNull(notEqualToken);
        Assert.Equal(12, notEqualToken.Column);

        Token lessEqualToken = tokens.Find(t => t.Type == TokenType.LessEqual)!;
        Assert.NotNull(lessEqualToken);
        Assert.Equal(18, lessEqualToken.Column);

        Token greaterEqualToken = tokens.Find(t => t.Type == TokenType.GreaterEqual)!;
        Assert.NotNull(greaterEqualToken);
        Assert.Equal(24, greaterEqualToken.Column);
    }

    [Fact]
    public void RaptorScriptCompiler_PropertyMappingTest()
    {
        string raptorScript =
            @"
var val = enemy.x + enemy.y;
enemy.z = val * 2.0;
";
        var propertyMappings = new Dictionary<string, int>
        {
            { "enemy.x", 1 },
            { "enemy.y", 2 },
            { "enemy.z", 3 },
        };

        string rasmCode = RaptorScriptCompiler.Compile(
            raptorScript,
            out var variables,
            new DiagnosticReporter(),
            propertyMappings
        );

        ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);

        // Create VM state and set registers 1 and 2 manually from C#
        var vm = new VirtualMachine();
        vm.LoadProgram(chunk);

        vm.SetRegister(1, 10.0); // enemy.x
        vm.SetRegister(2, 20.0); // enemy.y

        vm.RunFast();

        // Read register 3 (enemy.z) directly from C#
        double zVal = vm.GetRegister(3);
        Assert.Equal(60.0, zVal);

        // Check that temp variable 'val' got allocated in a safe register (index >= 4)
        int valReg = variables["val"];
        Assert.True(valReg >= 4);
    }

    [Fact]
    public void CompilerThrowsCompileExceptionOnSyntaxErrors()
    {
        string raptorScript =
            @"
var x = 10.0;
var y = ; // Syntax error
";
        var reporter = new DiagnosticReporter();
        Assert.Throws<CompileException>(() =>
            RaptorScriptCompiler.Compile(raptorScript, reporter: reporter)
        );
        Assert.True(reporter.HasErrors);
    }

    [Fact]
    public void CompilerRecoversFromMultipleUndefinedAndDoubleDeclarations()
    {
        string raptorScript =
            @"
var x = 10.0;
var x = 20.0; // Double declaration
var y = z + w; // Undefined identifiers
";
        var reporter = new DiagnosticReporter();
        Assert.Throws<CompileException>(() =>
            RaptorScriptCompiler.Compile(raptorScript, reporter: reporter)
        );

        Assert.True(reporter.HasErrors);
        // Verify multiple errors reported: E0019 (double declaration), E0018 (undefined z), E0018 (undefined w)
        var codes = reporter.Diagnostics.Select(d => d.Code).ToList();
        Assert.Contains("E0019", codes);
        Assert.Contains("E0018", codes);
        Assert.Equal(3, codes.Count);
    }

    [Fact]
    public void CompilerHandlesNonComparisonLoopConditions()
    {
        string raptorScript =
            @"
var sum = 0.0;
var running = 1.0;
for (var i = 0.0; running && i < 5.0; i = i + 1.0) {
    sum = sum + i;
    if (i == 2.0) {
        running = 0.0;
    }
}
";
        using var engine = new ScriptEngine();
        string rasm = RaptorScriptCompiler.Compile(raptorScript, out var vars, new DiagnosticReporter());
        VMChunk chunk = engine.Compile(rasm);
        ExecutionResult result = engine.Execute(chunk);
        Assert.Equal(VMStatus.Halted, result.Status);
        // i=0 (sum=0), i=1 (sum=1), i=2 (sum=3, running set to 0.0). Loop terminates before i=3.
        Assert.Equal(3.0, result.RegistersSnapshot[vars["sum"]]);
    }

    [Fact]
    public void CompilerThrowsEmitExceptionOnFfiArgCountErrors()
    {
        string raptorScript =
            @"
var a = alloc(1.0, 2.0); // alloc expects exactly 1 argument
";
        var reporter = new DiagnosticReporter();
        Assert.Throws<CompileException>(() =>
            RaptorScriptCompiler.Compile(raptorScript, reporter: reporter)
        );
        Assert.True(reporter.HasErrors);
        Assert.Contains("E0024", reporter.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void CompilerRecoversFromUndeclaredVariableAssignments()
    {
        string raptorScript =
            @"
x = 10.0; // Undeclared variable assignment
y = 20.0; // Undeclared variable assignment
";
        var reporter = new DiagnosticReporter();
        Assert.Throws<CompileException>(() =>
            RaptorScriptCompiler.Compile(raptorScript, reporter: reporter)
        );
        Assert.True(reporter.HasErrors);
        var codes = reporter.Diagnostics.Select(d => d.Code).ToList();
        Assert.Equal(2, codes.Count);
        Assert.All(codes, code => Assert.Equal("E0018", code));
    }

    [Fact]
    public void ComplexExpressionEvaluationTest()
    {
        string raptorScript = @"var result = 8 | 4 ^ 2 & 10 == 5 << 1 && 3 || 9;";
        var reporter = new DiagnosticReporter();
        string rasmCode = RaptorScriptCompiler.Compile(
            raptorScript,
            reporter: reporter
        );
        Assert.False(reporter.HasErrors);

        ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);

        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        ExecutionResult res = vm.RunFast();
        Assert.Equal(VMStatus.Halted, res.Status);
        // r1 is var result
        Assert.Equal(3.0, res.RegistersSnapshot[1]);
    }

    [Fact]
    public void InvertedForLoopConditionTest()
    {
        string raptorScript = @"
var sum = 0;
for (var j = 0; 5 > j; j = j + 1) {
    sum = sum + j;
}
";
        var reporter = new DiagnosticReporter();
        string rasmCode = RaptorScriptCompiler.Compile(raptorScript, reporter: reporter);
        Assert.False(reporter.HasErrors);

        ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);
        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        ExecutionResult res = vm.RunFast();
        Assert.Equal(VMStatus.Halted, res.Status);
        // sum = 0 + 1 + 2 + 3 + 4 = 10
        Assert.Equal(10.0, res.RegistersSnapshot[1]);
    }

    [Fact]
    public void LeftHandStepForLoopTest()
    {
        string raptorScript = @"
var sum = 0;
for (var i = 1; i < 5; i = 1 + i) {
    sum = sum + i;
}
";
        var reporter = new DiagnosticReporter();
        string rasmCode = RaptorScriptCompiler.Compile(raptorScript, reporter: reporter);
        Assert.False(reporter.HasErrors);

        ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);
        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        ExecutionResult res = vm.RunFast();
        Assert.Equal(VMStatus.Halted, res.Status);
        // sum = 1 + 2 + 3 + 4 = 10
        Assert.Equal(10.0, res.RegistersSnapshot[1]);
    }

    [Fact]
    public void SignedRightShiftNegativeNumberTest()
    {
        string raptorScript = @"
var folded = (0 - 4) >> 1;
var val = 0 - 4;
var runtime = val >> 1;
";
        var reporter = new DiagnosticReporter();
        string rasmCode = RaptorScriptCompiler.Compile(raptorScript, reporter: reporter);
        Assert.False(reporter.HasErrors);

        ScriptEngine engine = new ScriptEngine();
        VMChunk chunk = engine.Compile(rasmCode);
        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        ExecutionResult res = vm.RunFast();
        Assert.Equal(VMStatus.Halted, res.Status);
        Assert.Equal(-2.0, res.RegistersSnapshot[1]); // folded
        Assert.Equal(-2.0, res.RegistersSnapshot[3]); // runtime
    }

    [Fact]
    public void DoubleVarDeclInvariantFormattingTest()
    {
        var currentCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            string raptorScript = @"var d = 0.5;";
            var reporter = new DiagnosticReporter();
            string rasmCode = RaptorScriptCompiler.Compile(raptorScript, reporter: reporter);
            Assert.False(reporter.HasErrors);
            Assert.Contains("0.5", rasmCode);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = currentCulture;
        }
    }
}
