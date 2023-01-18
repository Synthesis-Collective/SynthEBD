using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;
public class StandaloneEnvironmentSource
{
    public string OutputModName { get; set; } = "SynthEBD";
    public string GameEnvironmentDirectory { get; set; } = "";
    public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
}

