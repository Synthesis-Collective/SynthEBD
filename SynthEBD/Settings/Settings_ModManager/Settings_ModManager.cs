namespace SynthEBD;

/// <summary>Settings POCO describing the host mod manager, persisted as JSON. Tells SynthEBD where
/// to install generated assets (MO2 vs. Vortex vs. none) and what filesystem path-length limit to
/// respect.</summary>
public class Settings_ModManager
{

    /// <summary>Which mod manager SynthEBD is installing into.</summary>
    public ModManager ModManagerType { get; set; } = ModManager.None;
    public MO2 MO2Settings { get; set; } = new();
    public Vortex VortexSettings { get; set; } = new();
    /// <summary>Default install target (the game Data folder), set in <see cref="Initialize"/>.</summary>
    public string DefaultInstallationFolder { get; set; }
    /// <summary>Currently selected install target; starts equal to <see cref="DefaultInstallationFolder"/>.</summary>
    public string CurrentInstallationFolder { get; set; }
    /// <summary>Scratch folder (beside the executable) for extracting downloaded archives.</summary>
    public string TempExtractionFolder { get; set; } = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "Temp");
    /// <summary>Max full-path length tolerated before warning about path-too-long issues.</summary>
    public int FilePathLimit { get; set; } = 260;

    /// <summary>Seeds the default and current installation folders from the resolved game Data folder.</summary>
    public void Initialize(IEnvironmentStateProvider environmentProvider)
    {
        DefaultInstallationFolder = environmentProvider.DataFolderPath;
        CurrentInstallationFolder = DefaultInstallationFolder;
    }

    /// <summary>Mod Organizer 2-specific paths and path-length limit.</summary>
    public class MO2
    {
        public string ExecutablePath { get; set; } = "";
        public string ModFolderPath { get; set; } = "";
        public int FilePathLimit { get; set; } = 220;
    }

    /// <summary>Vortex-specific staging path and path-length limit.</summary>
    public class Vortex
    {
        public string StagingFolderPath { get; set; } = "";
        public int FilePathLimit { get; set; } = 220;
    }
}

/// <summary>The mod manager SynthEBD targets when installing generated files.</summary>
public enum ModManager
{
    /// <summary>No mod manager; install directly to the Data folder.</summary>
    None,
    /// <summary>Mod Organizer 2.</summary>
    ModOrganizer2,
    /// <summary>Vortex.</summary>
    Vortex
}