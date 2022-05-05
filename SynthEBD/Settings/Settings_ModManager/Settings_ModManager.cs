namespace SynthEBD;

public class Settings_ModManager
{
    public Settings_ModManager()
    {
        ModManagerType = ModManager.None;
        DefaultInstallationFolder = PatcherEnvironmentProvider.Environment.DataFolderPath;
        CurrentInstallationFolder = DefaultInstallationFolder;
        MO2Settings = new MO2();
        VortexSettings = new Vortex();
        TempExtractionFolder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "Temp");
        FilePathLimit = 260;
    }

    public ModManager ModManagerType { get; set; }
    public MO2 MO2Settings { get; set; }
    public Vortex VortexSettings { get; set; }
    public string DefaultInstallationFolder { get; set; }
    public string CurrentInstallationFolder { get; set; }
    public string TempExtractionFolder { get; set; }
    public int FilePathLimit { get; set; }
    public class MO2
    {
        public MO2()
        {
            ExecutablePath = "";
            ModFolderPath = "";
            FilePathLimit = 220;
        }
        public string ExecutablePath { get; set; }
        public string ModFolderPath { get; set; }
        public int FilePathLimit { get; set; }
    }

    public class Vortex
    {
        public Vortex()
        {
            StagingFolderPath = "";
            FilePathLimit = 220;
        }
        public string StagingFolderPath { get; set; }
        public int FilePathLimit { get; set; }
    }
}

public enum ModManager
{
    None,
    ModOrganizer2,
    Vortex
}