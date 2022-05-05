namespace SynthEBD
{
    public class Manifest
    {
        public Manifest()
        {
            ConfigName = "New Config";
            ConfigDescription = "";
            DestinationModFolder = "Top Level Folder";
            ConfigPrefix = "Prefix";
            FileExtensionMap = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) { { "nif", "meshes" }, { "tri", "meshes" }, { "dds", "textures" } };
            AssetPackPaths = new HashSet<string>();
            RecordTemplatePaths = new HashSet<string>();
            BodyGenConfigPaths = new HashSet<string>();
            OptionsDescription = "";
            Options = new HashSet<Option>();
            DownloadInfo = new HashSet<DownloadInfoContainer>();
        }
        public string ConfigName { get; set; }
        public string ConfigDescription { get; set; }
        public string DestinationModFolder { get; set; }
        public string ConfigPrefix { get; set; }
        public Dictionary<string, string> FileExtensionMap { get; set; }
        public HashSet<string> AssetPackPaths { get; set; }
        public HashSet<string> RecordTemplatePaths { get; set; }
        public HashSet<string> BodyGenConfigPaths { get; set; }
        public HashSet<DownloadInfoContainer> DownloadInfo { get; set; }
        public string OptionsDescription { get; set; }
        public HashSet<Option> Options { get; set; }

        public class Option
        {
            public Option()
            {
                Name = "";
                Description = "";
                AssetPackPaths = new HashSet<string>();
                RecordTemplatePaths = new HashSet<string>();
                BodyGenConfigPaths = new HashSet<string>();
                DownloadInfo = new HashSet<DownloadInfoContainer>();
                OptionsDescription = "";
                Options = new HashSet<Option>();
                DestinationModFolder = "";
            }
            public string Name { get; set; }
            public string Description { get; set; }
            public HashSet<string> AssetPackPaths { get; set;}
            public HashSet<string> RecordTemplatePaths { get; set; }
            public HashSet<string> BodyGenConfigPaths { get; set; }
            public HashSet<DownloadInfoContainer> DownloadInfo { get; set; }
            public string OptionsDescription { get; set; }
            public HashSet<Option> Options { get; set; }
            public string DestinationModFolder { get; set; } // overwrites main if populated
        }

        public class DownloadInfoContainer
        {
            public DownloadInfoContainer()
            {
                ModPageName = "";
                URL = "";
                ExpectedFileName = "";
            }
            public string ModPageName { get; set; }
            public string ModDownloadName { get; set; }
            public string URL { get; set; }
            public string ExpectedFileName { get; set; }
        }
    }
}
