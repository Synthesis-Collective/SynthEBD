using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class Manifest
    {
        public Manifest()
        {
            ConfigName = "";
            ConfigDescription = "";
            DestinationModFolder = "";
            FileExtensionMap = new Dictionary<string, string>() { { "nif", "meshes" }, { "dds", "textures" } };
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
            }
            public string Name { get; set; }
            public string Description { get; set; }
            public HashSet<string> AssetPackPaths { get; set;}
            public HashSet<string> RecordTemplatePaths { get; set; }
            public HashSet<string> BodyGenConfigPaths { get; set; }
            public HashSet<DownloadInfoContainer> DownloadInfo { get; set; }
            public string OptionsDescription { get; set; }
            public HashSet<Option> Options { get; set; }
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
