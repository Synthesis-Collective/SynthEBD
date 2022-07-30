using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_Headparts
    {
        public Settings_HeadPartType EyebrowSettings { get; set; } = new();
        public Settings_HeadPartType EyeSettings { get; set; } = new();
        public Settings_HeadPartType FaceSettings { get; set; } = new();
        public Settings_HeadPartType FacialHairSettings { get; set; } = new();
        public Settings_HeadPartType HairSettings { get; set; } = new();
        public Settings_HeadPartType MiscSettings { get; set; } = new();
        public Settings_HeadPartType ScarsSettings { get; set; } = new()
        {
            DistributionProbabilities = new()
            {
                new HeadPartQuantityDistributionWeighting() { Quantity = 0, ProbabilityWeighting = 10 },
                new HeadPartQuantityDistributionWeighting() { Quantity = 1, ProbabilityWeighting = 10 },
                new HeadPartQuantityDistributionWeighting() { Quantity = 2, ProbabilityWeighting = 4 },
                new HeadPartQuantityDistributionWeighting() { Quantity = 3, ProbabilityWeighting = 1 }
            }
        };
    }
}
