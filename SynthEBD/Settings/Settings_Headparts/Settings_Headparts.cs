using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Settings_Headparts
    {
        public Dictionary<HeadPart.TypeEnum, Settings_HeadPartType> Types { get; set; } = new()
        {
            { HeadPart.TypeEnum.Eyebrows, new() },
            { HeadPart.TypeEnum.Eyes, new() },
            { HeadPart.TypeEnum.Face, new() },
            { HeadPart.TypeEnum.FacialHair, new() },
            { HeadPart.TypeEnum.Hair, new() },
            { HeadPart.TypeEnum.Misc, new() },
            { HeadPart.TypeEnum.Scars, new()
                {
                    DistributionProbabilities = new()
                    {
                        new HeadPartQuantityDistributionWeighting() { Quantity = 0, ProbabilityWeighting = 10 },
                        new HeadPartQuantityDistributionWeighting() { Quantity = 1, ProbabilityWeighting = 10 },
                        new HeadPartQuantityDistributionWeighting() { Quantity = 2, ProbabilityWeighting = 4 },
                        new HeadPartQuantityDistributionWeighting() { Quantity = 3, ProbabilityWeighting = 1 }
                    }
                }
            }
        };
    }
}
