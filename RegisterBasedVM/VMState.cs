using System.Text;

namespace RegisterBasedVM;

public unsafe struct VMState
{
    public double* RegPtr;
    public double* ConstPtr;
    public uint* MethodTablePtr;
    public uint* InstPtr;
    public byte* HeapPtr;
    public int Pc;
    public int BasePtr;
    public uint FreeBlockHeaderPointer;
    public StackFrame* CallStackPtr;
    public uint RngState;
    public StringBuilder StringBuilder;
}
