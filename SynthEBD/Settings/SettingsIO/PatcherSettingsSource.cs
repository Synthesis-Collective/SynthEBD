using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class PatcherSettingsSource
{
    public bool Initialized { get; set; } = false;
    public bool LoadFromDataDir { get; set; } = false;
    public string PortableSettingsFolder { get; set; } = "";
}