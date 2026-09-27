using System;
using System.Linq;
using Raptor;

namespace Raptor.AotSmoke;

/// <summary>
/// Publishes as NativeAOT and runs a program with control flow and a managed FFI host
/// call. This is the smoke test for the AOT-only deployment path: the library sets
/// <c>PublishAot=true</c>, so a trimmed/AOT build must still assemble, verify and run.
/// </summary>
internal static class Program
{
    private static unsafe int Main()
    {
        var chunk = new VMChunk();
        var assembler = new Assembler(chunk);
        assembler.RegisterHostMethod("double_it", 5);
        assembler.Parse(
            @"DEFINE n 5
LOADC r1 0
loop:
ADD r1 r1 1
LT 0 r1 n
JUMP loop
CALL double_it() r1
HALT"
                .Split('\n')
                .ToList()
        );

        BytecodeVerifier.Verify(chunk, 16 * 1024 * 1024);

        var vm = new VirtualMachine();
        vm.RegisterHostMethod(
            5,
            (ref VMState state) =>
            {
                state.RegPtr[0] *= 2.0;
            }
        );
        vm.LoadProgram(chunk);

        ExecutionResult result = vm.RunFast();
        if (result.Status != VMStatus.Halted || result.RegistersSnapshot[1] != 10.0)
        {
            Console.Error.WriteLine(
                $"AOT smoke FAILED: status={result.Status}, r1={result.RegistersSnapshot[1]}"
            );
            return 1;
        }

        Console.WriteLine("AOT smoke OK: r1=10");
        return 0;
    }
}
