namespace SynthEBD;

/// <summary>
/// Central registry of the 3-part (Layperson / Technical / Motivation) control documentation shown
/// as rich tooltips via <see cref="DocTooltip"/>. Entries are keyed "Menu.Element" (e.g.
/// "AssetPatching.FacePatchingMode") and registered by the per-menu partials
/// (UiDocs.Dashboard.cs, UiDocs.General.cs, UiDocs.AssetPatching.cs, UiDocs.BodyShape.cs).
/// A C# registry (rather than a resource file) keeps the keys compile-adjacent and the content
/// reviewable in ordinary diffs. Menus not yet migrated keep their legacy inline tooltips.
/// </summary>
public static partial class UiDocs
{
    private static readonly Dictionary<string, UiDocEntry> _entries = new();

    /// <summary>All registered entries, keyed "Menu.Element".</summary>
    public static IReadOnlyDictionary<string, UiDocEntry> All => _entries;

    static UiDocs()
    {
        RegisterDashboard();
        RegisterGeneral();
        RegisterAssetPatching();
        RegisterBodyShape();
        RegisterHeight();
        RegisterModManager();
        RegisterShell();
        RegisterBlockList();
        RegisterConsistency();
        RegisterSpecificAssignments();
        RegisterHeadParts();
        RegisterSubgroup();
        RegisterAssetPackEditor();
        RegisterBodyTypeProfiles();
        RegisterBodySlides();
        RegisterBodyGenEditor();
        RegisterCharacterViewer();
        RegisterDistributionTools();
    }

    private static void Add(string key, string layperson, string technical, string motivation)
    {
        _entries[key] = new UiDocEntry(layperson, technical, motivation);
    }
}
