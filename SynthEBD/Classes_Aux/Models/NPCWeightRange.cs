namespace SynthEBD;

/// <summary>An inclusive NPC weight range (0-100) used to scope rules to a band of the weight slider.</summary>
public class NPCWeightRange
{
    public int Lower { get; set; } = 0;
    public int Upper { get; set; } = 100;

    /// <summary>Returns a copy of this range.</summary>
    /// <returns>A new <see cref="NPCWeightRange"/> with the same bounds.</returns>
    public NPCWeightRange Clone()
    {
        return new NPCWeightRange() { Lower = Lower, Upper = Upper };
    }
}