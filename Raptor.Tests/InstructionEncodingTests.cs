using System.IO;
using System.Linq;
using Raptor;
using Xunit;

namespace Raptor.Tests;

/// <summary>
/// Pins the v2 instruction bit layout (op7 | A8 | B8 | C9) and the branch-offset
/// formats so a future encoding change cannot silently break packing/unpacking.
/// </summary>
public class InstructionEncodingTests
{
    [Fact]
    public void AbcGoldenLayout()
    {
        // op in bits 0..6, A in 7..14, B in 15..22, C in 23..31
        uint expected = (uint)OpCode.ADD | (3u << 7) | (5u << 15) | (260u << 23);
        Instruction inst = Instruction.CreateABC(OpCode.ADD, 3, 5, 260);

        Assert.Equal(expected, inst.Value);
        Assert.Equal(OpCode.ADD, inst.Op);
        Assert.Equal(3, inst.A);
        Assert.Equal(5, inst.B);
        Assert.Equal(260, inst.C);
    }

    [Fact]
    public void AbcBoundariesRoundTrip()
    {
        Instruction inst = Instruction.CreateABC(OpCode.MUL, 255, 255, 511);
        Assert.Equal(OpCode.MUL, inst.Op);
        Assert.Equal(255, inst.A);
        Assert.Equal(255, inst.B);
        Assert.Equal(511, inst.C);
    }

    [Fact]
    public void AbxGoldenLayoutAndBoundary()
    {
        uint expected = (uint)OpCode.LOADC | (7u << 7) | (131071u << 15);
        Instruction inst = Instruction.CreateABx(OpCode.LOADC, 7, 131071);

        Assert.Equal(expected, inst.Value);
        Assert.Equal(OpCode.LOADC, inst.Op);
        Assert.Equal(7, inst.A);
        Assert.Equal(131071u, inst.Bx);
    }

    [Theory]
    [InlineData(-65535)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65536)]
    public void SignedBx17RoundTrips(int offset)
    {
        Instruction inst = Instruction.CreateAsBx(OpCode.FOR, 0, offset);
        Assert.Equal(offset, inst.sBx17);
    }

    [Theory]
    [InlineData(-16777215)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16777216)]
    public void SignedBx25RoundTrips(int offset)
    {
        Instruction inst = Instruction.CreateSBx25(OpCode.JUMP, offset);
        Assert.Equal(offset, inst.sBx25);
    }

    [Fact]
    public void BinaryRoundTripPreservesChunkAndBehaviour()
    {
        string asm =
            @"LOADC r1 10.0
ADD r2 r1 r1
JUMP end
ADD r3 r1 r1
end:
HALT";
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split('\n').ToList());

        using var stream = new MemoryStream();
        RaptorBinary.Save(chunk, stream);
        stream.Position = 0;
        VMChunk loaded = RaptorBinary.Load(stream);

        Assert.Equal(chunk.Instructions, loaded.Instructions);
        Assert.Equal(chunk.Constants, loaded.Constants);
        Assert.Equal(chunk.MethodTable, loaded.MethodTable);

        double[] fromOriginal = Run(chunk);
        double[] fromLoaded = Run(loaded);
        Assert.Equal(fromOriginal, fromLoaded);
    }

    [Fact]
    public void DisassemblerRendersKnownProgram()
    {
        string asm =
            @"LOADC r1 10.0
LOADC r2 5.5
ADD r3 r1 r2
HALT";
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split('\n').ToList());

        string text = Disassembler.Disassemble(chunk);
        Assert.Contains("LOADC r1 10", text);
        Assert.Contains("LOADC r2 5.5", text);
        Assert.Contains("ADD r3 r1 r2", text);
        Assert.Contains("HALT", text);
    }

    private static double[] Run(VMChunk chunk)
    {
        VirtualMachine vm = new VirtualMachine();
        vm.LoadProgram(chunk);
        ExecutionResult result = vm.RunFast();
        Assert.Equal(VMStatus.Halted, result.Status);
        return result.RegistersSnapshot;
    }
}