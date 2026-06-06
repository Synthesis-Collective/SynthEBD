using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Helper that makes a random yes/no decision weighted by a percentage probability.
    /// </summary>
    public class BoolByProbability
    {
        /// <summary>
        /// Returns a random boolean that is <c>true</c> with the given probability.
        /// </summary>
        /// <param name="trueProbability">The chance of returning <c>true</c>, expressed on a 0-100 percentage scale.</param>
        /// <returns><c>true</c> if a random draw in [0,100) falls at or below <paramref name="trueProbability"/>; otherwise <c>false</c>.</returns>
        public static bool Decide(double trueProbability)
        {
            Random gen = new Random();
            int prob = gen.Next(100);
            return prob <= trueProbability;
        }
    }
}
