namespace RegisterBasedVM;

public class Assembler
{
    private readonly VMChunk _chunk;

    public Assembler(VMChunk chunk)
    {
        _chunk = chunk;
    }

    public IEnumerable<string> CleanFirstPass(string[] rawLines)
    {
        foreach (string rawLine in rawLines)
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("DEFINE "))
                continue;
            yield return line;
        }
    }

    public IEnumerable<string> CleanLines(string[] rawLines)
    {
        foreach (string rawLine in rawLines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("DEFINE "))
                continue;

            if (line.EndsWith(":"))
                if (!line.StartsWith("JUMP"))
                    continue;

            if (line.EndsWith("()"))
                if (!line.StartsWith("JUMP"))
                    continue;
            yield return line;
        }
    }

    public void Parse(string[] lines)
    {
        Dictionary<string, string> definitions = new();

        //Having a 3 pass system will make it easier for future me to implement multi line definitions if I want to
        for (int i = 0; i < lines.Length; i++)
        {
            string[] words = lines[i].Split(" ");
            if (lines[i].StartsWith("DEFINE"))
            {
                if (!definitions.TryAdd(words[1], words[2]))
                    throw new Exception(
                        $"Definition for {words[1]} already exists on line {definitions[words[1]]}"
                    );
            }
            else
            {
                if (lines[i].Contains(";"))
                {
                    lines[i] = lines[i].Split(";")[0].Trim();
                }

                for (int j = 0; j < words.Length; j++)
                {
                    foreach (var pair in definitions)
                    {
                        if (words[j] == pair.Key)
                            words[j] = pair.Value;
                    }
                }
                lines[i] = string.Join(" ", words);
            }
        }
        Dictionary<string, uint> labels = new();
        Dictionary<string, uint> methods = new();
        uint memoryAddress = 0;
        uint methodAddress = 0;
#if DEBUG
        Console.WriteLine("After first pass");
#endif
        foreach (var item in CleanFirstPass(lines))
        {
#if DEBUG
            Console.WriteLine(item);
#endif
            if (item.EndsWith(":"))
            {
                if (!item.StartsWith("JUMP "))
                {
                    if (!labels.TryAdd(item.TrimEnd(':'), memoryAddress))
                        throw new Exception("Label name already exists on line " + labels[item]);
                }
            }
            else if (item.EndsWith("()"))
            {
                if (!lines.StartsWith("CALL"))
                {
                    if (!methods.TryAdd(item, memoryAddress))
                        throw new Exception(
                            "Method name already exists on line "
                                + _chunk.MethodTable[methods[item]]
                        );
                    else
                    {
                        _chunk.MethodTable[methodAddress++] = memoryAddress;
                    }
                }
            }
            else
                memoryAddress++;
        }
        List<UInt32> instructions = new List<UInt32>();
        int pc = 0;
#if DEBUG
        Console.WriteLine("After second pass");
#endif
        foreach (var item in CleanLines(lines))
        {
#if DEBUG
            Console.WriteLine(item);
#endif
            var words = item.Split();
            uint instruction = 0;
            uint opcode = 0;
            try
            {
                switch (words[0])
                {
                    case "LOADC":
                        opcode = (uint)OpCode.LOADC;

                        var destA1 = uint.Parse(string.Join("", words[1].Skip(1)));
                        float constant = float.Parse(words[2]);

                        uint bx = _chunk.SetConstant(constant);

                        destA1 = destA1 & 0xFF;
                        bx = bx & 0x3FFFF;

                        // Smash them together! Hehehehaw
                        instruction = opcode | (destA1 << 6) | (bx << 14);
                        break;
                    case "MOVE":
                        opcode = (uint)OpCode.MOVE;
                        uint destA2 = uint.Parse(string.Join("", words[1].Skip(1)));
                        destA2 = destA2 & 0xFF;

                        uint destB2 = uint.Parse(string.Join("", words[2].Skip(1)));
                        destB2 = destB2 & 0x1FF;
                        instruction = opcode | (destA2 << 6) | (destB2 << 14);
                        break;
                    case "SWP":
                        opcode = (uint)OpCode.SWAP;
                        uint destA5 = uint.Parse(words[1]);
                        destA5 = destA5 & 0xFF;

                        uint destB5 = uint.Parse(words[2]);
                        destB5 = destB5 & 0x1FF;
                        instruction = opcode | (destA5 << 6) | (destB5 << 14);
                        break;

                    case "ADD":
                    case "SUB":
                    case "MUL":
                    case "DIV":
                    case "POW":
                        opcode = GetOpCode(words[0]);
                        uint destA3;
                        if (words[1].StartsWith("r"))
                        {
                            destA3 = uint.Parse(words[1].TrimStart('r'));
                        }
                        else
                        {
                            destA3 = _chunk.SetConstant(float.Parse(words[1])) + 256;
                        }
                        destA3 = destA3 & 0xFF;

                        uint destB3;
                        if (words[2].StartsWith("r"))
                        {
                            destB3 = uint.Parse(words[2].TrimStart('r'));
                        }
                        else
                        {
                            destB3 = _chunk.SetConstant(float.Parse(words[2])) + 256;
                        }
                        destB3 = destB3 & 0x1FF;

                        uint destC3;
                        if (words[3].StartsWith("r"))
                        {
                            destC3 = uint.Parse(words[3].TrimStart('r'));
                        }
                        else
                        {
                            destC3 = _chunk.SetConstant(float.Parse(words[3])) + 256;
                        }
                        destC3 = destC3 & 0x1FF;

                        instruction = opcode | (destA3 << 6) | (destB3 << 14) | (destC3 << 23);
                        break;
                    case "EQ":
                    case "LT":
                    case "LE":
                        opcode = GetOpCode(words[0]);
                        destA3 = uint.Parse(words[1]);

                        if (words[2].StartsWith("r"))
                        {
                            destB3 = uint.Parse(words[2].TrimStart('r'));
                        }
                        else
                        {
                            destB3 = _chunk.SetConstant(float.Parse(words[2])) + 256;
                        }
                        destB3 = destB3 & 0x1FF;

                        if (words[3].StartsWith("r"))
                        {
                            destC3 = uint.Parse(words[3].TrimStart('r'));
                        }
                        else
                        {
                            destC3 = _chunk.SetConstant(float.Parse(words[3])) + 256;
                        }
                        destC3 = destC3 & 0x1FF;
                        instruction = opcode | (destA3 << 6) | (destB3 << 14) | (destC3 << 23);
                        break;
                    case "UNM":
                        opcode = (uint)OpCode.UNM;
                        var destA4 = uint.Parse(words[1].TrimStart('r'));
                        destA4 = destA4 & 0xFF;

                        uint destB4;
                        if (words[2].StartsWith("r"))
                        {
                            destB4 = uint.Parse(words[2].TrimStart('r'));
                        }
                        else
                        {
                            destB4 = _chunk.SetConstant(float.Parse(words[1])) + 256;
                        }

                        destB4 = destB4 & 0x1FF;
                        instruction = opcode | (destA4 << 6) | (destB4 << 14);
                        break;
                    case "JUMP":
                        opcode = (uint)OpCode.JUMP;
                        int labelIndex;
                        try
                        {
                            labelIndex = (int)(labels[words[1]] - pc);
                            Console.WriteLine(labelIndex);
                        }
                        catch
                        {
                            Console.WriteLine("There isnt a label with name " + words[1]);
                            return;
                        }
                        const int sBxBias = 33554431;

                        int rawSbx = labelIndex + sBxBias;
                        uint unsignedBx = (uint)(rawSbx & 0x3FFFFFF);
                        instruction = opcode | (uint)((unsignedBx << 6)); // |(((sBx >> 25) << 31)));
                        break;
                    case "CALL":
                        opcode = (uint)OpCode.CALL;
                        byte methodIndex = 0;
                        try
                        {
                            methodIndex = (byte)methods[words[1]];
                        }
                        catch
                        {
                            Console.WriteLine("There isnt a method with name " + words[1]);
                            return;
                        }
                        byte start = byte.Parse(words[2].TrimStart('r'));
                        rawSbx = methodIndex + sBxBias;
                        instruction =
                            opcode
                            | (uint)((methodIndex << 6) & 0x1FF)
                            | (uint)((start << 15) & 0xFF);
                        break;
                    case "RETURN":
                        opcode = (uint)OpCode.RETURN;
                        start = byte.Parse(words[1].TrimStart('r'));
                        byte end = byte.Parse(words[2].TrimStart('r'));
                        instruction =
                            opcode | (uint)((start << 6) & 0xFF) | (uint)((end << 14) & 0xFF);
                        break;
                    case "PRINT":
                        opcode = (uint)OpCode.PRINT;

                        uint printA;
                        if (words[1].StartsWith("r"))
                        {
                            printA = uint.Parse(words[1].TrimStart('r'));
                        }
                        else
                        {
                            printA = _chunk.SetConstant(float.Parse(words[1])) + 256;
                        }
                        printA = printA & 0x1FF;

                        instruction = opcode | (printA << 6);
                        break;
                    case "RAND":
                        opcode = (uint)OpCode.RAND;
                        uint randR = uint.Parse(words[1].TrimStart('r'));
                        instruction = opcode | (randR << 6);
                        break;
                    case "PRINTA":
                        opcode = (uint)OpCode.PRINTA;

                        uint printA1;
                        if (words[1].StartsWith("r"))
                        {
                            printA1 = uint.Parse(words[1].TrimStart('r'));
                        }
                        else
                        {
                            printA1 = _chunk.SetConstant(float.Parse(words[1])) + 256;
                        }
                        printA1 = printA1 & 0x1FF;

                        instruction = opcode | (printA1 << 6);
                        break;

                    case "HALT":
                        instruction = (uint)OpCode.HALT;
                        break;
                    case "SQRT":
                        opcode = (uint)OpCode.SQRT;
                        destA4 = uint.Parse(words[1].TrimStart('r'));
                        destA4 = destA4 & 0xFF;
                        if (words[2].StartsWith("r"))
                        {
                            destB4 = uint.Parse(words[2].TrimStart('r'));
                        }
                        else
                        {
                            destB4 = _chunk.SetConstant(float.Parse(words[2])) + 256;
                        }
                        destB4 = destB4 & 0x1FF;
                        instruction = opcode | (destA4 << 6) | (destB4 << 14);
                        break;
                    case "FISR":
                        opcode = (uint)OpCode.FISR;

                        destA4 = uint.Parse(words[2].TrimStart('r'));
                        destA4 = destA4 & 0xFF;
                        if (words[2].StartsWith("r"))
                        {
                            destB4 = uint.Parse(words[2].TrimStart('r'));
                        }
                        else
                        {
                            destB4 = _chunk.SetConstant(float.Parse(words[2])) + 256;
                        }
                        destB4 = destB4 & 0x1FF;
                        instruction = opcode | (destA4 << 6) | (destB4 << 14);
                        break;
                    default:
                        throw new Exception($"Unknown opcode found: {words[0]} on line {pc}");
                }
            }
            catch (Exception x)
            {
                Console.WriteLine($"{x.Message} at line {pc}");
            }
            instructions.Add(instruction);
            pc++;
        }
        _chunk.Instructions = instructions.ToArray();
    }

    private uint GetOpCode(string instruc)
    {
        switch (instruc)
        {
            case "ADD":
                return (uint)OpCode.ADD;
            case "SUB":
                return (uint)OpCode.SUB;
            case "MUL":
                return (uint)OpCode.MUL;
            case "DIV":
                return (uint)OpCode.DIV;
            case "POW":
                return (uint)OpCode.POW;
            case "EQ":
                return (uint)OpCode.EQ;
            case "LT":
                return (uint)OpCode.LT;
            case "LE":
                return (uint)OpCode.LE;
            default:
                return 0xFF;
        }
    }
}
