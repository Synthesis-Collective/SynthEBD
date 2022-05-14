namespace SynthEBD;

public class Manifest
{
    public string ConfigName { get; set; } = "New Config";
    public string ConfigDescription { get; set; } = "";
    public string DestinationModFolder { get; set; } = "Top Level Folder";
    public string ConfigPrefix { get; set; } = "Prefix";
    public Dictionary<string, string> FileExtensionMap { get; set; } = new(StringComparer.InvariantCultureIgnoreCase) { { "nif", "meshes" }, { "tri", "meshes" }, { "dds", "textures" } };
    public HashSet<string> AssetPackPaths { get; set; } = new();
    public HashSet<string> RecordTemplatePaths { get; set; } = new();
    public HashSet<string> BodyGenConfigPaths { get; set; } = new();
    public HashSet<DownloadInfoContainer> DownloadInfo { get; set; } = new();
    public string OptionsDescription { get; set; } = "";
    public HashSet<Option> Options { get; set; } = new();

    public class Option
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public HashSet<string> AssetPackPaths { get; set;} = new();
        public HashSet<string> RecordTemplatePaths { get; set; } = new();
        public HashSet<string> BodyGenConfigPaths { get; set; } = new();
        public HashSet<DownloadInfoContainer> DownloadInfo { get; set; } = new();
        public string OptionsDescription { get; set; } = "";
        public HashSet<Option> Options { get; set; } = new();
        public string DestinationModFolder { get; set; } = ""; // overwrites main if populated
    }

    public class DownloadInfoContainer
    {
        public string ModPageName { get; set; } = "";
        public string ModDownloadName { get; set; }
        public string URL { get; set; } = "";
        public string ExpectedFileName { get; set; } = "";
    }
}