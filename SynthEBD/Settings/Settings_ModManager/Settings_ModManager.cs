namespace SynthEBD;

public class Settings_ModManager
{

    public ModManager ModManagerType { get; set; } = ModManager.None;
    public MO2 MO2Settings { get; set; } = new();
    public Vortex VortexSettings { get; set; } = new();
    public string DefaultInstallationFolder { get; set; }
    public string CurrentInstallationFolder { get; set; }
    public string TempExtractionFolder { get; set; } = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "Temp");
    public int FilePathLimit { get; set; } = 260;

    public void Initialize(IEnvironmentStateProvider stateProvider)
    {
        DefaultInstallationFolder = stateProvider.DataFolderPath;
        CurrentInstallationFolder = DefaultInstallationFolder;
    }

    public class MO2
    {
        public string ExecutablePath { get; set; } = "";
        public string ModFolderPath { get; set; } = "";
        public int FilePathLimit { get; set; } = 220;
    }

    public class Vortex
    {
        public string StagingFolderPath { get; set; } = "";
        public int FilePathLimit { get; set; } = 220;
    }
}

public enum ModManager
{
    None,
    ModOrganizer2,
    Vortex
}