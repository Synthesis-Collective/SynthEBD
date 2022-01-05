using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public static class PatcherSettings
    {
        public static Settings_General General { get; set; }
        public static Settings_TexMesh TexMesh { get; set; }
        public static Settings_BodyGen BodyGen { get; set; }
        public static Settings_Height Height { get; set; }
        public static Settings_ModManager ModManagerIntegration { get; set; }
        public static Paths Paths { get; set; }
    }
}