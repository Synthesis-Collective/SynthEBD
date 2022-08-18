namespace SynthEBD;

public class NPCWeightRange
{
    public int Lower { get; set; } = 0;
    public int Upper { get; set; } = 100;

    public NPCWeightRange Clone()
    {
        return new NPCWeightRange() { Lower = Lower, Upper = Upper };
    }
}