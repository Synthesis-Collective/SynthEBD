using Mutagen.Bethesda.Skyrim;

namespace SynthEBD
{
    public static class PatcherSettings
    {
        public static Settings_General General { get; set; }
        public static Settings_TexMesh TexMesh { get; set; }
        public static Settings_BodyGen BodyGen { get; set; }
        public static Settings_OBody OBody { get; set; }
        public static Settings_Height Height { get; set; }
        public static Settings_ModManager ModManagerIntegration { get; set; }
        public static Paths Paths { get; set; }
        public static bool LoadFromDataFolder { get; set; }
        public static string CustomGamePath { get; set; }
        public static string PortableSettingsFolder { get; set; }
        public static SkyrimRelease SkyrimVersion { get; set; }
    }
}