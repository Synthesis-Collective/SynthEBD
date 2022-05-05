namespace SynthEBD
{
    public interface IProbabilityWeighted
    {
        double ProbabilityWeighting { get; set; }
    }

    public class ProbabilityWeighting
    {
        public static IProbabilityWeighted SelectByProbability(IEnumerable<IProbabilityWeighted> inputs)
        {
            if (!inputs.Any()) { return null; }

            double totalWeight = inputs.Sum(x => x.ProbabilityWeighting);
            var randomCap = new Random().NextDouble() * totalWeight;

            double currentWeight = 0;
            foreach (var input in inputs)
            {
                currentWeight += input.ProbabilityWeighting;
                if (currentWeight >= randomCap)
                {
                    return input;
                }
            }

            // function should always return by this point. Leaving the original (used for ints) below as a fallback

            var inputList = inputs.ToList();

            HashSet<int> weightedSet = new HashSet<int>();
            for (int i = 0; i < inputList.Count; i++)
            {
                for (int j = 0; j < inputList[i].ProbabilityWeighting; j++)
                {
                    weightedSet.Add(i);
                }
            }

            return inputList[new Random().Next(weightedSet.Count)];
        }
    }
}
