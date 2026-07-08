public struct FreeBlock
{
    public uint HeapAddress;
    public uint Size;

    public FreeBlock(uint heapAddress, uint size)
    {
        HeapAddress = heapAddress;
        Size = size;
    }
}
