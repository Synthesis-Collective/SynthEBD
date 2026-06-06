using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;
/// <summary>
/// Small persisted DTO holding the parameters needed to construct the Mutagen game environment when SynthEBD
/// runs standalone (outside the Synthesis pipeline): the output mod name, game data directory, and Skyrim
/// release. Defaults target Skyrim SE with output mod "SynthEBD".
/// </summary>
public class StandaloneEnvironmentSource
{
    public string OutputModName { get; set; } = "SynthEBD";
    public string GameEnvironmentDirectory { get; set; } = "";
    public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
}

