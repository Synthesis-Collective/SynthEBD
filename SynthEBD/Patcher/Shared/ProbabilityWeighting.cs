using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public interface IProbabilityWeighted
    {
        int ProbabilityWeighting { get; set; }
    }

    public class ProbabilityWeighting
    {
        public static IProbabilityWeighted SelectByProbability(IEnumerable<IProbabilityWeighted> inputs)
        {
            if (!inputs.Any()) { return null; }

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
