using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;
/// <summary>Helpers for obtaining a record's EditorID (or a readable fallback) without risking null-reference errors.</summary>
public static class EditorIDHandler
{
    /// <summary>Returns a record's EditorID, or a descriptive placeholder when it is null.</summary>
    /// <param name="getter">Record to inspect; may be null.</param>
    /// <returns>The EditorID; "Null Record" when <paramref name="getter"/> is null; or "&lt;FormKey&gt; (No EditorID)" when present but unnamed.</returns>
    public static string GetEditorIDSafely(IMajorRecordGetter getter)
    {
        if (getter == null) { return "Null Record"; }
        return getter.EditorID ?? (getter.FormKey.ToString() + " (No EditorID)");
    }
    /// <summary>Resolves a FormKey through a link cache and returns the target record's EditorID, or a descriptive placeholder.</summary>
    /// <typeparam name="TType">Record type to resolve as.</typeparam>
    /// <param name="formKey">FormKey to resolve.</param>
    /// <param name="linkCache">Link cache used for resolution.</param>
    /// <returns>The EditorID; "&lt;FormKey&gt; (No EditorID)" when resolved but unnamed; or "&lt;FormKey&gt; (Not In Current Load Order)" when resolution fails.</returns>
    public static string GetEditorIDSafely<TType>(FormKey formKey, ILinkCache linkCache) where TType : class, IMajorRecordGetter
    {
        if (linkCache.TryResolve<TType>(formKey, out var getter))
        {
            return getter.EditorID ?? (getter.FormKey.ToString() + " (No EditorID)");
        }
        else
        {
            return formKey.ToString() + " (Not In Current Load Order)";
        }
    }
}
