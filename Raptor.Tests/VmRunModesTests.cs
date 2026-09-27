using System.Collections.Generic;
using System.Linq;
using Raptor;
using Xunit;

namespace Raptor.Tests;

/// <summary>
/// The three run loops (RunFast, RunProfile, RunDebug) share the opcode handlers but
/// have separate VMState setup. These tests keep them equivalent and make sure they can
/// actually execute control flow (RunProfile/RunDebug once forgot to set Gas, so the
/// first JUMP threw GasExceeded).
/// </summary>
public class VmRunModesTests
{
    // r1 counts 0 -> 5; the loop runs while r1 < 5.
    private const string CountToFiveAsm =
        @"LOADC r1 0
loop:
ADD r1 r1 1
LT 0 r1 5
JUMP loop
HALT";

    private static VMChunk Chunk(string asm)
    {
        VMChunk chunk = new VMChunk();
        new Assembler(chunk).Parse(asm.Split('\n').ToList());
        BytecodeVerifier.Verify(chunk, 16 * 1024 * 1024);
        return chunk;
    }

    private static VirtualMachine Loaded(string asm, ulong gas = 1_000_000_000)
    {
        VirtualMachine vm = new VirtualMachine(gasLimit: gas);
        vm.LoadProgram(Chunk(asm));
        return vm;
    }

    [Fact]
    public void RunProfileExecutesControlFlow()
    {
        VirtualMachine vm = Loaded(CountToFiveAsm);
        ExecutionResult result = vm.RunProfile(new ulong[64], out ulong total);

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(5.0, result.RegistersSnapshot[1]);
        Assert.True(total > 0, "RunProfile should have counted instructions");
    }

    [Fact]
    public void RunDebugExecutesControlFlow()
    {
        VirtualMachine vm = Loaded(CountToFiveAsm);
        int hookCalls = 0;
        ExecutionResult result = vm.RunDebug(
            (ref VMState state, Instruction instruction) =>
            {
                hookCalls++;
            }
        );

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(5.0, result.RegistersSnapshot[1]);
        Assert.True(hookCalls > 0);
    }

    [Fact]
    public void AllRunModesAgree()
    {
        ExecutionResult fast = Loaded(CountToFiveAsm).RunFast();

        VirtualMachine profileVm = Loaded(CountToFiveAsm);
        ExecutionResult profile = profileVm.RunProfile(new ulong[64], out ulong total);

        int hookCalls = 0;
        ExecutionResult debug = Loaded(CountToFiveAsm)
            .RunDebug((ref VMState state, Instruction instruction) => hookCalls++);

        Assert.Equal(VMStatus.Halted, fast.Status);
        Assert.Equal(VMStatus.Halted, profile.Status);
        Assert.Equal(VMStatus.Halted, debug.Status);
        Assert.Equal(fast.RegistersSnapshot, profile.RegistersSnapshot);
        Assert.Equal(fast.RegistersSnapshot, debug.RegistersSnapshot);

        // The debug hook fires once per dispatched instruction, including HALT.
        Assert.Equal(total, (ulong)hookCalls);
    }

    [Theory]
    [InlineData("RunFast")]
    [InlineData("RunProfile")]
    [InlineData("RunDebug")]
    public void GasLimitIsEnforcedInEveryRunMode(string mode)
    {
        VirtualMachine vm = Loaded(CountToFiveAsm, gas: 3);
        ExecutionResult result = mode switch
        {
            "RunFast" => vm.RunFast(),
            "RunProfile" => vm.RunProfile(new ulong[64], out _),
            _ => vm.RunDebug((ref VMState state, Instruction instruction) => { }),
        };

        Assert.Equal(VMStatus.GasExceeded, result.Status);
    }
}