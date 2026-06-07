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
        /// <returns><c>true</c> if a random draw in [0,100) falls below <paramref name="trueProbability"/>; otherwise <c>false</c>.</returns>
        public static bool Decide(double trueProbability)
        {
            // Random.Shared is thread-safe and avoids a per-call allocation. A continuous draw in [0,100)
            // compared with '<' makes Decide(0) never true, Decide(100) always true, and Decide(T) exactly
            // T% (including fractional T) — unlike the old gen.Next(100) <= T, which ran +1% high per bucket
            // (e.g. Decide(0) was true ~1% of the time) and truncated fractional probabilities.
            return Random.Shared.NextDouble() * 100.0 < trueProbability;
        }
    }
}
