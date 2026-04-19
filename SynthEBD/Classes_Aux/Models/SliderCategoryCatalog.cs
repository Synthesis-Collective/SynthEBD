using System.Collections.Generic;
using Newtonsoft.Json;

namespace SynthEBD;

/// <summary>
/// In-memory representation of a SliderCategories.xml file (or its shipped JSON twin).
/// One <see cref="BodyTypeCatalog"/> per body type (e.g. CBBE, HIMBO, BHUNP).
/// </summary>
public class BodyTypeCatalog
{
    /// <summary>Canonical body type name as displayed in the UI (e.g. "CBBE", "HIMBO").</summary>
    public string BodyType { get; set; } = "";

    /// <summary>Gender this body type targets. UBE-style multi-gender entries are not handled in stage 4.</summary>
    public Gender Gender { get; set; } = Gender.Female;

    /// <summary>Set of slider names declared in the catalog. Compared case-insensitively against preset sliders.</summary>
    public HashSet<string> Sliders { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Other body types this entry is interchangeable with. Used by the classifier to merge results
    /// across compatible families (e.g. 3BA / 3BBB / CBBE).
    /// </summary>
    public HashSet<string> Family { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Aggregate catalog: a dictionary of body type name -> <see cref="BodyTypeCatalog"/>. Loaded once at
/// startup by <see cref="SliderCatalogLoader"/> and consumed by <see cref="BodySlideGroupClassifier"/>.
/// </summary>
public class SliderCategoryCatalog
{
    public Dictionary<string, BodyTypeCatalog> BodyTypes { get; set; }
        = new(System.StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Shipped JSON shape for fallback catalogs in InternalData/SliderCatalogs/{BodyType}.json.
/// Kept separate from <see cref="BodyTypeCatalog"/> so on-disk format stays decoupled from runtime model.
/// </summary>
public class SliderCatalogFile
{
    public string BodyType { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public List<string> Sliders { get; set; } = new();
    public List<string> Family { get; set; } = new();
}

/// <summary>
/// Persisted entry in the user-extensible Body-Type Registry.
/// Each entry describes one parent body type (e.g. "CBBE 3BA") by the files that prove it's
/// installed (<see cref="IdentityFingerprints"/>) and the ShapeData subfolders whose OSD/BSD
/// files define its slider catalog (<see cref="ShapeDataFolders"/>).
/// Slider names and superset relationships are derived live at load time -- never persisted --
/// so the registry stays in sync as body mods update.
/// </summary>
public class BodyTypeRegistryEntry
{
    /// <summary>Canonical body-type name shown in UI and stored on presets (e.g. "CBBE 3BA").</summary>
    public string Name { get; set; } = "";

    /// <summary>Gender this body targets. Male/Female classification flows from this field only.</summary>
    public Gender Gender { get; set; } = Gender.Female;

    /// <summary>
    /// Data-folder-relative paths whose existence proves this body is installed.
    /// Forward or back slashes are accepted. Any single hit flips <see cref="IsInstalled"/>.
    /// Directories or files are both valid targets.
    /// </summary>
    public List<string> IdentityFingerprints { get; set; } = new();

    /// <summary>
    /// Paths relative to CalienteTools/BodySlide/ShapeData pointing at this body's reference
    /// slider data. Each entry is either a single .osd/.bsd file (preferred -- the body's
    /// reference mesh, which defines the canonical slider catalog) or a folder scanned
    /// recursively (for UUNP-style bodies that ship per-slider .bsd files in a flat layout).
    /// Point at the reference body only, not outfit folders: outfit OSDs pollute the slider
    /// set with armor-specific morphs and break cross-body subset detection.
    /// </summary>
    public List<string> ShapeDataFolders { get; set; } = new();

    /// <summary>
    /// Name of another registry entry this one extends (e.g. "CBBE 3BA" → "CBBE"). Auto-filled
    /// by superset detection after slider extraction. Empty string when no parent is detected.
    /// </summary>
    public string SupersetOfBodyType { get; set; } = "";

    /// <summary>
    /// True for entries the user has authored or modified in the UI. Protects them from being
    /// overwritten when shipped defaults update. Shipped entries leave this false.
    /// </summary>
    public bool IsUserDefined { get; set; } = false;

    /// <summary>Runtime-only: set by <c>BodyTypeFingerprintScanner</c> once per load.</summary>
    [JsonIgnore] public bool IsInstalled { get; set; }

    /// <summary>
    /// Runtime-only: union of slider names parsed from every OSD/BSD file in
    /// <see cref="ShapeDataFolders"/>. Authoritative catalog for classification.
    /// </summary>
    [JsonIgnore] public HashSet<string> ResolvedSliders { get; set; } =
        new(System.StringComparer.OrdinalIgnoreCase);
}
