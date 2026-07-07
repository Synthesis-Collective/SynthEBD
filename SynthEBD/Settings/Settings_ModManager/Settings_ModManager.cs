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
    public string TempExtractionFolder { get; set; } = GetDefaultTempExtractionFolder();
    /// <summary>Max full-path length tolerated before warning about path-too-long issues.</summary>
    public int FilePathLimit { get; set; } = 260;

    /// <summary>Resolves the default scratch folder beside the executable, falling back to the app base
    /// directory when the entry assembly location is unavailable (e.g. single-file publish) so it never throws.</summary>
    private static string GetDefaultTempExtractionFolder()
    {
        var entryLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var baseDir = !string.IsNullOrEmpty(entryLocation) ? System.IO.Path.GetDirectoryName(entryLocation) : null;
        return System.IO.Path.Combine(baseDir ?? System.AppContext.BaseDirectory, "Temp");
    }

    /// <summary>Seeds the default and current installation folders from the resolved game Data folder.</summary>
    public void Initialize(IEnvironmentStateProvider environmentProvider)
    {
        DefaultInstallationFolder = environmentProvider.DataFolderPath;
        CurrentInstallationFolder = DefaultInstallationFolder;
    }

    /// <summary>Mod Organizer 2-specific paths and path-length limit.</summary>
    public class MO2
    {
        /// <summary>Path to the MO2 launcher. In <see cref="LinuxMode"/> this instead holds the
        /// full path to ModOrganizer.ini directly, since the Linux MO2 port (Fluorine) ships no
        /// executable. Either way it is only used to locate ModOrganizer.ini.</summary>
        public string ExecutablePath { get; set; } = "";
        public string ModFolderPath { get; set; } = "";
        public int FilePathLimit { get; set; } = 220;
        /// <summary>When true, <see cref="ExecutablePath"/> is treated as a direct path to
        /// ModOrganizer.ini rather than to ModOrganizer.exe, enabling MO2 integration on the
        /// Linux MO2 port (Fluorine), which has no executable.</summary>
        public bool LinuxMode { get; set; } = false;
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