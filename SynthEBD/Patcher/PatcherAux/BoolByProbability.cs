using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BoolByProbability
    {
        public static bool Decide(double trueProbability)
        {
            Random gen = new Random();
            int prob = gen.Next(100);
            return prob <= trueProbability;
        }
    }
}
