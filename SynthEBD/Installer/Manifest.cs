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
            AssetPackPaths = new HashSet<string>();
            RecordTemplatePaths = new HashSet<string>();
            BodyGenConfigPaths = new HashSet<string>();
            Options = new HashSet<Option>();
            MainDownloadInfo = new HashSet<DownloadInfo>();
        }
        public string ConfigName { get; set; }
        public string ConfigDescription { get; set; }
        public HashSet<string> AssetPackPaths { get; set; }
        public HashSet<string> RecordTemplatePaths { get; set; }
        public HashSet<string> BodyGenConfigPaths { get; set; }
        public HashSet<DownloadInfo> MainDownloadInfo { get; set; }
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
                DownloadInfo = new HashSet<DownloadInfo>();
                Options = new HashSet<Option>();
            }
            public string Name { get; set; }
            public string Description { get; set; }
            public HashSet<string> AssetPackPaths { get; set;}
            public HashSet<string> RecordTemplatePaths { get; set; }
            public HashSet<string> BodyGenConfigPaths { get; set; }
            public HashSet<DownloadInfo> DownloadInfo { get; set; }
            public HashSet<Option> Options { get; set; }
        }

        public class DownloadInfo
        {
            public DownloadInfo()
            {
                URL = "";
                ExpectedFileName = "";
            }
            public string URL { get; set; }
            public string ExpectedFileName { get; set; }
        }
    }
}
