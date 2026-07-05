namespace RegisterBasedVM;

public class VMChunk
{
    private uint currUsedConstantsIndex = 0;
    public UInt32[] Instructions { get; set; }
    public double[] Constants { get; private set; } = new double[512];
    public uint[] MethodTable { get; private set; } = new uint[512];

    public uint SetConstant(float value)
    {
        var index = Constants.IndexOf(value);
        if (index != -1)
        {
            return (uint)index;
        }
        Constants[currUsedConstantsIndex] = value;
        return currUsedConstantsIndex++;
    }
}
