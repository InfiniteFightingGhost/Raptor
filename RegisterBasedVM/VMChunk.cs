namespace RegisterBasedVM;

public class VMChunk
{
    public UInt32[] Instructions { get; set; }
    public float[] Constants { get; private set; } = new float[512];
    public int ConstantCount { get; set; }

    public void SetConstant(float value, UInt32 index)
    {
        if (ConstantCount == Constants.Length)
            UpgradeConstantArraySize();
        Constants[index] = value;
    }

    public void UpgradeConstantArraySize()
    {
        float[] buf = new float[Constants.Length * 2];
        for (int i = 0; i < Constants.Length; i++)
        {
            buf[i] = Constants[i];
        }
        Constants = buf;
    }
}
