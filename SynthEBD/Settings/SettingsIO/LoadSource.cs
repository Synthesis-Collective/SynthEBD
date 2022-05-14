using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class LoadSource
{
    public bool LoadFromDataDir { get; set; } = false;
    public string GameEnvironmentDirectory { get; set; } = "";
    public string PortableSettingsFolder { get; set; } = "";
    public SkyrimRelease SkyrimVersion { get; set; } = SkyrimRelease.SkyrimSE;
}