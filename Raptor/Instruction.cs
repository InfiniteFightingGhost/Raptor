using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Raptor
{
    ///<summary>
    ///The struct that unifies all bit fiddling logic into a simple and bug-free solution.
    ///</summary>
    ///<remarks>
    ///Every bytecode packing and unpacking needs to go through here.
    ///No manual bit fiddling is allowed for instructions.
    ///</remarks>
    public readonly struct Instruction
    {
        public readonly uint Value;

        private const int OpCodeBits = 7;
        private const int ABits = 8;
        private const int BBits = 8;
        private const int CBits = 9;
        private const int BxBits = 17;
        private const int sBx17Bits = 17;
        private const int sBx25Bits = 25;

        private const int AShift = OpCodeBits;
        private const int BShift = AShift + ABits;
        private const int CShift = BShift + BBits;

        private const uint OpCodeMask = (1 << OpCodeBits) - 1;
        private const uint AMask = (1 << ABits) - 1;
        private const uint BMask = (1 << BBits) - 1;
        private const uint CMask = (1 << CBits) - 1;
        private const uint BxMask = (1 << BxBits) - 1;
        private const uint sBx17Mask = (1 << sBx17Bits) - 1;
        private const uint sBx25Mask = (1 << sBx25Bits) - 1;

        public Instruction(uint value) => Value = value;

        public OpCode Op => (OpCode)(Value & OpCodeMask);
        public byte A => (byte)((Value >> AShift) & AMask);
        public ushort B => (ushort)((Value >> BShift) & BMask);
        public ushort C => (ushort)((Value >> CShift) & CMask);
        public uint Bx => (Value >> BShift) & BxMask;
        private const int sBx16Bias = 32767;
        public int sBx17 => (int)((Value >> BShift) & sBx17Mask) - sBx16Bias;

        private const int sBx25Bias = 16777216;
        public int sBx25 => (int)((Value >> AShift) & sBx25Mask) - sBx25Bias;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Instruction CreateABC(OpCode op, byte a, ushort b, ushort c)
        {
            uint val =
                (uint)op
                | (((uint)a & AMask) << AShift)
                | (((uint)b & BMask) << BShift)
                | (((uint)c & CMask) << CShift);
            return new Instruction(val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Instruction CreateABx(OpCode op, byte a, uint bx)
        {
            uint val = (uint)op | ((uint)a << AShift) | (bx << BShift);
            return new Instruction(val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Instruction CreateAsBx(OpCode op, byte a, int sbx)
        {
            uint biasedBx = (uint)(sbx + sBx16Bias) & sBx17Mask;
            uint val = (uint)op | ((uint)a << AShift) | (biasedBx << BShift);
            return new Instruction(val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Instruction CreateSBx25(OpCode op, int sBxOffset)
        {
            uint biasedBx = (uint)(sBxOffset + sBx25Bias) & 0x1FFFFFF;

            uint val = (uint)op | (biasedBx << AShift);

            return new Instruction(val);
        }

        public static implicit operator uint(Instruction i) => i.Value;
    }
}
