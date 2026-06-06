using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Small persisted DTO describing where SynthEBD should read its settings from: whether to use a portable
/// (user-specified) settings folder versus the default location. Read early in startup to resolve the
/// settings root before the main models are loaded.
/// </summary>
public class PatcherSettingsSource
{
    public bool Initialized { get; set; } = false;
    public bool UsePortableSettings { get; set; } = false;
    public string PortableSettingsFolder { get; set; } = "";
}