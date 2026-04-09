using System.Collections.Generic;

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
