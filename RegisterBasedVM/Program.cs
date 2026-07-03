using RegisterBasedVM;

string instructions =
    @"LOADC 0 1
LOADC 1 0
LOADC 3 50
LOADC 4 1
LOADC 5 1
PRINT 4
PRINT 0
MOVE 2 1
MOVE 1 0
ADD 0 1 2
ADD 4 4 5
LT 1 4 3
JUMP -7
HALT";

VMChunk chunk = new VMChunk();
Assembler ass = new(chunk);
ass.Parse(instructions.Split("\n"));

VirtualMachine vm = new();

vm.LoadProgram(chunk.Instructions, chunk.Constants);
vm.Run();
