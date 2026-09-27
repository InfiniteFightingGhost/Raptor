using System;
using System.Linq;
using Raptor;
using Xunit;

namespace Raptor.Tests;

/// <summary>
/// The v2 encoding makes the B operand slot register-only (8-bit). A constant there
/// must fail loudly at assemble time instead of being silently truncated into a
/// register index — the exact class of bug that is otherwise very hard to trace.
/// </summary>
public class AssemblerGuardTests
{
    private static void Assemble(string asm)
    {
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split('\n').ToList());
    }

    [Theory]
    [InlineData("ADD r1 5.0 r2")]
    [InlineData("SUB r1 1.0 r2")]
    [InlineData("MUL r1 2.0 r2")]
    [InlineData("DIV r1 2.0 r2")]
    [InlineData("MOD r1 2.0 r2")]
    [InlineData("POW r1 2.0 r2")]
    [InlineData("BINAND r1 2.0 r2")]
    [InlineData("BINOR r1 2.0 r2")]
    [InlineData("BINXOR r1 2.0 r2")]
    [InlineData("BINLSH r1 2.0 r2")]
    [InlineData("BINRSH r1 2.0 r2")]
    [InlineData("SETARR r1 0 r2")]
    [InlineData("SETARRA r1 0 r2")]
    [InlineData("LT 1 5.0 r2")]
    [InlineData("LE 1 5.0 r2")]
    [InlineData("EQ 1 5.0 r2")]
    [InlineData("JLT 1 5.0 r2 loop")]
    [InlineData("UNM r1 5.0")]
    [InlineData("SQRT r1 5.0")]
    [InlineData("FISR r1 5.0")]
    [InlineData("PRINT 5.0")]
    [InlineData("PRINTA 5.0")]
    [InlineData("NEWARR r1 100")]
    [InlineData("LENARR r1 5.0")]
    public void ConstantInRegisterOnlyBSlotIsRejected(string op)
    {
        string asm = "loop:\n" + op + "\nHALT";
        var ex = Assert.Throws<Exception>(() => Assemble(asm));
        Assert.Contains("register", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForWithConstantMaxIsRejected()
    {
        var ex = Assert.Throws<Exception>(() => Assemble("loop:\nFOR r1 10 1 < loop\nHALT"));
        Assert.Contains("register", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterBeyond255IsRejected()
    {
        var ex = Assert.Throws<Exception>(() => Assemble("ADD r1 r256 r2\nHALT"));
        Assert.Contains("register", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstantInRightHandCSlotStillAssembles()
    {
        // C is the RC slot, so a constant belongs there and must keep working.
        Assemble("LOADC r1 1.0\nADD r2 r1 5.0\nHALT");
    }
}