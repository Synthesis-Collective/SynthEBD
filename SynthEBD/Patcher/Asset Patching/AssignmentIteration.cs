namespace SynthEBD;

/// <summary>
/// Contains the permutation and asssociated data generated by one iteration of the GenerateCombination() function
/// By tracking previously-generated combinations, the patcher avoids entering an infinite loop of re-generating combinations trying to find one that satisfied subgroup and bodygen constraints
/// </summary>
public class AssignmentIteration
{
    public AssignmentIteration()
    {
        PreviouslyGeneratedCombinations = new HashSet<string>();
    }
    public List<FlattenedSubgroup> AvailableSeeds { get; set; } = new();
    public FlattenedSubgroup ChosenSeed { get; set; } = null;
    public FlattenedAssetPack ChosenAssetPack { get; set; } = null;
    public Dictionary<int, FlattenedAssetPack> RemainingVariantsByIndex { get; set; } = new();
    public HashSet<string> PreviouslyGeneratedCombinations = new HashSet<string>();

    public static int BackTrack(AssignmentIteration iterationInfo, FlattenedSubgroup toRemove, int currentIndex, int steps)
    {
        FlattenedAssetPack revertTo = iterationInfo.RemainingVariantsByIndex[currentIndex - steps];

        if (toRemove != null)
        {
            revertTo.Subgroups[currentIndex - steps].Remove(toRemove);
        }
        iterationInfo.ChosenAssetPack = revertTo;

        return currentIndex - steps - 1; // -1 because the calling for loop will then immediately add 1 back at the next iteration
    }
}