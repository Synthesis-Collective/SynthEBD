using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class LoadSource
    {
        public LoadSource()
        {
            LoadFromDataDir = false;
            GameEnvironmentDirectory = "";
            PortableSettingsFolder = "";
            SkyrimVersion = SkyrimRelease.SkyrimSE;
        }
        public bool LoadFromDataDir { get; set; }
        public string GameEnvironmentDirectory { get; set; }
        public string PortableSettingsFolder { get; set; }
        public SkyrimRelease SkyrimVersion { get; set; }
    }
}
