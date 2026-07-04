using RegisterBasedVM;

string instructions = @"";

VMChunk chunk = new VMChunk();
Assembler ass = new(chunk);
ass.Parse(instructions.Split("\n"));

VirtualMachine vm = new();
Console.WriteLine(chunk.Instructions.Count());
vm.LoadProgram(chunk.Instructions, chunk.Constants, new int[] { 0 });
vm.RunFast();
