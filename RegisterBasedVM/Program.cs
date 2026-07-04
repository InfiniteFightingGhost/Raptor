using RegisterBasedVM;

string instructions =
    @"DEFINE result r0
DEFINE last r1
DEFINE lastlast r2
DEFINE counter r4
DEFINE n 10
LOADC result 1
LOADC counter 1
loop:
    MOVE lastlast last
    MOVE last result
    ADD result last lastlast
    ADD counter counter 1
    LT 1 counter n
    JUMP loop
PRINT result
HALT";

VMChunk chunk = new VMChunk();
Assembler ass = new(chunk);
ass.Parse(instructions.Split("\n"));

VirtualMachine vm = new();
Console.WriteLine(chunk.Instructions.Count());
vm.LoadProgram(chunk.Instructions, chunk.Constants, new int[] { });
vm.RunFast();
