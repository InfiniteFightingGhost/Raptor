using System;
using System.Collections.Generic;
using System.Linq;
using Raptor;
using Xunit;
using static Raptor.VirtualMachine;

namespace Raptor.Tests;

public class FfiAndMemoryTests
{
    [Fact]
    public void FfiAndDirectMemoryAccessTest()
    {
        VMChunk testChunk = new VMChunk();
        Assembler testAss = new(testChunk);

        testAss.RegisterHostMethod("addNumbers", 5);
        testAss.RegisterHostMethod("verifySpan", 6);
        testAss.RegisterHostMethod("verifyModifiedSpan", 7);

        string testScript =
            @"
LOADC r1 42.0
LOADC r2 58.0
CALL addNumbers() r1
PRINT r1

NEWARR r3 8
LOADC r4 0
LOADC r5 999.0
SETARR r3 r4 r5

CALL verifySpan() r3
CALL verifyModifiedSpan() r3
HALT
";

        VirtualMachine testVm = new VirtualMachine();

        testVm.RegisterHostMethod(
            5,
            (ref VMState state) =>
            {
                unsafe
                {
                    double a = state.RegPtr[0];
                    double b = state.RegPtr[1];
                    state.RegPtr[0] = a + b;
                }
            }
        );

        testVm.RegisterHostMethod(
            6,
            (ref VMState state) =>
            {
                unsafe
                {
                    double ptrVal = state.RegPtr[0];
                    Span<double> span = testVm.GetDoubleSpan(ptrVal, 1);
                    Assert.Equal(999.0, span[0]);
                    span[0] = 777.0;
                }
            }
        );

        testVm.RegisterHostMethod(
            7,
            (ref VMState state) =>
            {
                unsafe
                {
                    double ptrVal = state.RegPtr[0];
                    Span<double> span = testVm.GetDoubleSpan(ptrVal, 1);
                    Assert.Equal(777.0, span[0]);
                }
            }
        );

        testAss.Parse(testScript.Split("\n").ToList());
        BytecodeVerifier.Verify(testChunk, 1024);
        testVm.LoadProgram(testChunk);
        ExecutionResult result = testVm.RunFast();

        Assert.Equal(VMStatus.Halted, result.Status);
    }

    internal static unsafe void UnmanagedFfiAdd(ref VMState state)
    {
        double a = state.RegPtr[0];
        double b = state.RegPtr[1];
        state.RegPtr[0] = a + b + 10.0;
    }

    [Fact]
    public unsafe void UnmanagedFfiFunctionPointerTest()
    {
        VMChunk testChunk = new VMChunk();
        Assembler testAss = new(testChunk);

        testAss.RegisterHostMethod("unmanagedAdd", 12);

        string testScript =
            @"
LOADC r1 15.0
LOADC r2 25.0
CALL unmanagedAdd() r1
PRINT r1
HALT
";

        VirtualMachine testVm = new VirtualMachine();

        delegate* managed<ref VMState, void> fp = &UnmanagedFfiAdd;
        testVm.RegisterHostMethod(12, fp);

        testAss.Parse(testScript.Split("\n").ToList());
        BytecodeVerifier.Verify(testChunk, 1024);
        testVm.LoadProgram(testChunk);
        ExecutionResult result = testVm.RunFast();

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(50.0, testVm.GetRegister(1));
    }

    [Fact]
    public unsafe void ScriptEngineUnmanagedFfiTest()
    {
        var table = new FFIHostTable();
        table.Register("unmanagedAdd", 12, (ref VMState state) => UnmanagedFfiAdd(ref state));

        var vm = new VirtualMachine();
        vm.RegisterHostTable(table);

        var engine = new ScriptEngine();
        engine.RegisterHostTable(table);

        string testScript =
            @"
LOADC r1 50.0
LOADC r2 30.0
CALL unmanagedAdd() r1
PRINT r1
HALT
";

        var chunk = engine.Compile(testScript);
        vm.LoadProgram(chunk);
        var result = vm.RunFast();

        Assert.Equal(VMStatus.Halted, result.Status);
        Assert.Equal(90.0, vm.GetRegister(1));
    }
}
