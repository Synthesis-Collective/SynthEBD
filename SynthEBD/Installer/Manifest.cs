namespace SynthEBD;

public class Manifest
{
    public string ConfigName { get; set; } = "New Config";
    public string ConfigDescription { get; set; } = string.Empty;
    
    public string ConfigPrefix { get; set; } = "Prefix";
    
    public List<Option> Options { get; set; } = new();
    public string InstallationMessage { get; set; } = string.Empty;
    public int Version { get; set; }
    public Dictionary<string, string> FileExtensionMap { get; set; } = new(StringComparer.InvariantCultureIgnoreCase); // legacy property to support original format
    public HashSet<DownloadInfoContainer> DownloadInfo { get; set; } = new(); // legacy property to support original format
    public string DestinationModFolder { get; set; } = ""; // legacy property to support original format
    public string OptionsDescription { get; set; } = ""; // legacy property to support original format
    public HashSet<string> AssetPackPaths { get; set; } = new(); // legacy property to support original format
    public HashSet<string> RecordTemplatePaths { get; set; } = new(); // legacy property to support original format
    public HashSet<string> BodyGenConfigPaths { get; set; } = new(); // legacy property to support original format
    public HashSet<Mutagen.Bethesda.Plugins.FormKey> AddPatchableRaces { get; set; } = new(); // not legacy, but using same pattern as other properties

    public class Option
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public HashSet<string> AssetPackPaths { get; set;} = new();
        public HashSet<string> RecordTemplatePaths { get; set; } = new();
        public HashSet<string> BodyGenConfigPaths { get; set; } = new();
        public HashSet<DownloadInfoContainer> DownloadInfo { get; set; } = new();
        public Dictionary<string, string> FileExtensionMap { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);
        public string OptionsDescription { get; set; } = "";
        public List<Option> Options { get; set; } = new();
        public string DestinationModFolder { get; set; } = ""; // overwrites main if populated
        public HashSet<Mutagen.Bethesda.Plugins.FormKey> AddPatchableRaces { get; set; } = new();
    }

    public class DownloadInfoContainer
    {
        public string ModPageName { get; set; } = "";
        public string ModDownloadName { get; set; }
        public string URL { get; set; } = "";
        public string ExpectedFileName { get; set; } = "";
    }
}