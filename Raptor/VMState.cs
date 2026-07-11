using System.Runtime.InteropServices;

namespace Raptor;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct VMState
{
    public double* RegPtr;
    public double* ConstPtr;
    public uint* MethodTablePtr;
    public uint* InstPtr;
    public uint* Ip;
    public byte* HeapPtr;
    public int BasePtr;
    public uint FreeBlockHeaderPointer;
    public StackFrame* CallStackPtr;
    public StackFrame* CallStackLimit;
    public uint RngState;
    public char* OutBufferPtr;
    public int OutBufferCapacity;
    public int OutBufferOffset;
}
