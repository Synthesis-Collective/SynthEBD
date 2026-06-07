namespace SynthEBD;

/// <summary>
/// Data-transfer/serialization model describing a downloadable, shareable config "pack". Authored by the
/// packager (<see cref="VM_Manifest"/>), serialized to Manifest.json at the root of a config archive, and parsed
/// by <see cref="ConfigInstaller"/> at install time. Carries the config's metadata, install <see cref="Option"/>
/// tree, file-extension routing, dependency download links, and destination folder. Several top-level members are
/// legacy fields retained for the pre-0.8.3 (Version 0) single-root format; the installer/packager VMs migrate
/// these into a root <see cref="Option"/> on load.
/// </summary>
public class Manifest
{
    public string ConfigName { get; set; } = "New Config";
    public string ConfigDescription { get; set; } = string.Empty;
    
    /// <summary>Second directory segment expected in every asset path (e.g. textures\PREFIX\...); used to strip/restore
    /// the install prefix when copying files. Required; validated by <see cref="ConfigInstaller.ValidateManifest"/>.</summary>
    public string ConfigPrefix { get; set; } = "Prefix";
    
    /// <summary>Top-level install option chains. Each entry is one selection step presented sequentially in the wizard.</summary>
    public List<Option> Options { get; set; } = new();
    public string InstallationMessage { get; set; } = string.Empty;
    /// <summary>Manifest schema version. 0 = legacy single-root format (migrated on load); 1 = current multi-option format.</summary>
    public int Version { get; set; }
    public Dictionary<string, string> FileExtensionMap { get; set; } = new(StringComparer.InvariantCultureIgnoreCase); // legacy property to support original format
    public HashSet<DownloadInfoContainer> DownloadInfo { get; set; } = new(); // legacy property to support original format
    public string DestinationModFolder { get; set; } = ""; // legacy property to support original format
    public string OptionsDescription { get; set; } = ""; // legacy property to support original format
    public HashSet<string> AssetPackPaths { get; set; } = new(); // legacy property to support original format
    public HashSet<string> RecordTemplatePaths { get; set; } = new(); // legacy property to support original format
    public HashSet<string> BodyGenConfigPaths { get; set; } = new(); // legacy property to support original format
    /// <summary>Race FormKeys the installer offers to add to the user's Patchable Races list.</summary>
    public HashSet<Mutagen.Bethesda.Plugins.FormKey> AddPatchableRaces { get; set; } = new(); // not legacy, but using same pattern as other properties
    /// <summary>Referenced asset paths that may legitimately be absent; suppresses the "missing source file" warning for these.</summary>
    public HashSet<string> IgnoreMissingSourceFiles { get; set; } = new(); // not legacy, but using same pattern as other properties

    /// <summary>One user-selectable install branch. Carries the resources (asset packs, BodyGen configs, record
    /// templates, downloads) contributed if chosen, plus nested <see cref="Options"/> for multi-step selection.
    /// The chosen chain is unioned into the parent <see cref="Manifest"/> at finalize time.</summary>
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
        public HashSet<string> IgnoreMissingSourceFiles { get; set; } = new();
        public List<Option> Options { get; set; } = new();
        public string DestinationModFolder { get; set; } = ""; // overwrites main if populated
        public HashSet<Mutagen.Bethesda.Plugins.FormKey> AddPatchableRaces { get; set; } = new();
    }

    /// <summary>Describes one external dependency archive the user must supply manually: where to get it (mod page,
    /// URL, expected file name) and where its contents extract within the install tree (<see cref="ExtractionSubPath"/>,
    /// an alternate prefix overriding <see cref="ConfigPrefix"/>).</summary>
    public class DownloadInfoContainer
    {
        public string ModPageName { get; set; } = "";
        public string ModDownloadName { get; set; }
        public string URL { get; set; } = "";
        public string ExpectedFileName { get; set; } = "";
        public string ExtractionSubPath { get; set; } = "";
    }
}