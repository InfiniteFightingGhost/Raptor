using Raptor;

namespace Raptor.Tests;

public class FusedBranchTests
{
    private static ExecutionResult Run(string asm)
    {
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split("\n").ToList());
        BytecodeVerifier.Verify(chunk, 16 * 1024 * 1024);
        VirtualMachine machine = new VirtualMachine();
        machine.LoadProgram(chunk);
        return machine.RunFast();
    }

    [Fact]
    public void FusedLoopProducesSameResultAsLegacyLoop()
    {
        const string legacy =
            @"DEFINE result r0
DEFINE last r1
DEFINE lastlast r2
DEFINE counter r4
DEFINE n 5000
LOADC result 1
LOADC counter 1
loop:
    MOVE lastlast last
    MOVE last result
    ADD result last lastlast
    ADD counter counter 1
    LT 0 counter n
    JUMP loop
HALT";

        string fused = legacy.Replace(
            "    LT 0 counter n\n    JUMP loop",
            "    JLT 0 counter n loop"
        );

        ExecutionResult legacyResult = Run(legacy);
        ExecutionResult fusedResult = Run(fused);

        Assert.Equal(VMStatus.Halted, legacyResult.Status);
        Assert.Equal(VMStatus.Halted, fusedResult.Status);
        Assert.Equal(legacyResult.RegistersSnapshot[0], fusedResult.RegistersSnapshot[0]);
    }

    [Theory]
    [InlineData("LOADC r1 5.0\nJLT 1 r1 10.0", 1.0)] // 5 < 10 -> jump skipped, fall through to LOADC 1
    [InlineData("LOADC r1 10.0\nJLT 1 r1 5.0", 0.0)]
    [InlineData("LOADC r1 5.0\nJLE 1 r1 5.0", 1.0)]
    [InlineData("LOADC r1 7.0\nJEQ 1 r1 7.0", 1.0)]
    [InlineData("LOADC r1 7.0\nJEQ 0 r1 7.0", 0.0)]
    [InlineData("LOADC r1 5.0\nJLT 0 r1 10.0", 0.0)] // expected=false, (5<10)!=false -> jump to skip
    public void FusedComparisonPolarityIsCorrect(string branch, double expected)
    {
        string asm =
            $@"LOADC r0 0.0
{branch} target
LOADC r0 1.0
target:
HALT";
        ExecutionResult result = Run(asm);
        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(expected, result.RegistersSnapshot[0]);
    }

    [Fact]
    public void FusedBranchConsumesGasLikeJump()
    {
        const string infinite =
            @"loop:
JLT 1 r0 r0 loop
HALT";
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(infinite.Split("\n").ToList());
        BytecodeVerifier.Verify(chunk, 1024);

        VirtualMachine machine = new VirtualMachine(gasLimit: 5, heapSize: 256 * 1024);
        machine.LoadProgram(chunk);
        ExecutionResult result = machine.RunFast();

        Assert.Equal(VMStatus.GasExceeded, result.Status);
    }

    [Fact]
    public void JumpIntoFusedPayloadIsRejectedByVerifier()
    {
        VMChunk badChunk = new VMChunk();
        badChunk.Instructions = new uint[]
        {
            Instruction.CreateSBx25(OpCode.JUMP, 2), // lands on the payload word at index 2
            Instruction.CreateABC(OpCode.JLT, 1, 0, 0),
            Instruction.CreateSBx25(OpCode.JLT, 0),
            Instruction.CreateABC(OpCode.HALT, 0, 0, 0),
        };
        Assert.Throws<VerificationException>(() => BytecodeVerifier.Verify(badChunk, 1024));
    }

    [Fact]
    public void DisassemblerRendersFusedBranchWithoutDesync()
    {
        string asm =
            @"LOADC r0 0.0
loop:
JLT 0 r0 10.0 loop
LOADC r0 1.0
HALT";
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split("\n").ToList());

        string text = Disassembler.Disassemble(chunk);
        Assert.Contains("JLT", text);
        Assert.Contains("HALT", text);
    }
}
