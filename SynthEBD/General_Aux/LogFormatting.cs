using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Pure, stateless string formatters for log output: timestamps, XML-report indentation, and human-readable
/// renderings of records, subgroups, races, and body-shape descriptors. Extracted from <see cref="Logger"/>
/// (R12) so the format helpers can be unit-tested in isolation; <see cref="Logger"/> keeps thin static
/// forwarders to these so existing call sites are unaffected.
/// </summary>
public static class LogFormatting
{
    /// <summary>Formats a timestamp as a bracketed "[HH:MM:SS] " prefix for log lines.</summary>
    /// <param name="dt">The time to format.</param>
    /// <returns>e.g. "[14:03:09] ".</returns>
    public static string FormatTimeStamp(DateTime dt)
    {
        return "[" + DateTimeToHMS(dt) + "] ";
    }

    /// <summary>Formats the time-of-day portion of <paramref name="dt"/> as zero-padded "HH:MM:SS".</summary>
    /// <param name="dt">The time to format.</param>
    /// <returns>e.g. "14:03:09".</returns>
    public static string DateTimeToHMS(DateTime dt)
    {
        return string.Format("{0:D2}:{1:D2}:{2:D2}", dt.Hour, dt.Minute, dt.Second);
    }

    /// <summary>Pretty-prints an XML report string by inserting line breaks between adjacent tags and indenting each line by tag depth.</summary>
    /// <param name="s">Serialized XML (typically from <see cref="System.Xml.Linq.XDocument.ToString()"/>).</param>
    /// <returns>The same XML with tab indentation reflecting element nesting.</returns>
    /// <remarks>Hand-rolled formatter that splits on "&gt;&lt;", so text content containing that sequence could be mis-split. An <see cref="System.Xml.XmlWriter"/> with indentation enabled would be more robust.</remarks>
    public static string FormatLogStringIndents(string s)
    {
        int indent = 0;

        s = s.Replace("><", ">" + Environment.NewLine + "<");

        string[] split = s.Split(Environment.NewLine);
        for (int i = 0; i < split.Length; i++)
        {
            if (split[i].Trim().StartsWith("</"))
            {
                indent--;
                split[i] = Indent(split[i], indent);
            }
            else if (split[i].Trim().StartsWith('<') && !split[i].Trim().EndsWith("/>"))
            {
                split[i] = Indent(split[i], indent);
                indent++;
            }
            else
            {
                split[i] = Indent(split[i], indent);
            }
        }

        return string.Join(Environment.NewLine, split);
    }

    /// <summary>Prefixes <paramref name="s"/> with <paramref name="count"/> tab characters.</summary>
    /// <param name="s">Line to indent.</param>
    /// <param name="count">Number of tabs to prepend (values &lt;= 0 leave the string unchanged).</param>
    /// <returns>The indented line.</returns>
    private static string Indent(string s, int count)
    {
        if (count <= 0) { return s; }
        return new string('\t', count) + s;
    }

    /// <summary>Builds a multi-line listing of an asset pack's subgroups (one line per position, each showing that position's candidate subgroup IDs), optionally indenting one position.</summary>
    /// <param name="ap">The flattened asset pack to describe.</param>
    /// <param name="index">Subgroup position to optionally highlight with indentation.</param>
    /// <param name="indentAtIndex">When <c>true</c>, indents the line at <paramref name="index"/>.</param>
    /// <returns>A newline-delimited description used in verbose logs.</returns>
    public static string SpreadFlattenedAssetPack(FlattenedAssetPack ap, int index, bool indentAtIndex)
    {
        string spread = Environment.NewLine;
        for (int i = 0; i < ap.Subgroups.Count; i++)
        {
            if (indentAtIndex && i == index) { spread += "\t"; }
            spread += i + ": [" + String.Join(',', ap.Subgroups[i].Select(x => x.Id)) + "]" + Environment.NewLine;
        }
        return spread;
    }

    /// <summary>Builds a "Name | EditorID | FormKey" identifier string for an NPC.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <param name="logger">Logger used for safe name resolution.</param>
    /// <returns>A pipe-delimited identifier for logs.</returns>
    public static string GetNPCLogNameString(INpcGetter npc, Logger logger)
    {
        return NameHandler.GetNPCNameSafely(npc, logger) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    /// <summary>Builds a filesystem-safe "Name (EditorID) FormKey" string for naming per-NPC report files.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <returns>A sanitized identifier safe for use as a file name.</returns>
    public static string GetNPCLogReportingString(INpcGetter npc)
    {
        return IO_Aux.MakeValidFileName(npc.Name?.String + " (" + EditorIDHandler.GetEditorIDSafely(npc) + ") " + npc.FormKey.ToString().Replace(':', '-'));
    }

    /// <summary>Formats a flattened subgroup as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(FlattenedSubgroup subgroup)
    {
        return subgroup.Id + ": " + subgroup.Name;
    }

    /// <summary>Formats an asset-pack subgroup model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(AssetPack.Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a subgroup view model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(VM_Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a placeholder subgroup view model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(VM_SubgroupPlaceHolder subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a category-to-values descriptor map as "Category: [v1, v2] | Category2: [...]".</summary>
    /// <param name="descriptorList">Map of descriptor category to its values.</param>
    /// <returns>A single-line, pipe-delimited descriptor summary.</returns>
    public static string GetBodyShapeDescriptorString(Dictionary<string, HashSet<string>> descriptorList)
    {
        List<string> sections = new List<string>();
        foreach (var descriptor in descriptorList)
        {
            string section = descriptor.Key + ": [";
            section += string.Join(", ", descriptor.Value);
            section += "]";
            sections.Add(section);
        }
        return string.Join(" | ", sections);
    }

    /// <summary>Formats a set of body-shape descriptor label signatures, grouping values by category, one category per line.</summary>
    /// <typeparam name="T">A body-shape descriptor label-signature type.</typeparam>
    /// <param name="descriptors">The descriptors to format.</param>
    /// <returns>A multi-line "Category: [values]" summary.</returns>
    public static string GetBodyShapeDescriptorString<T>(HashSet<T> descriptors)
        where T : BodyShapeDescriptor.LabelSignature
    {
        var categories = descriptors.Select(x => x.Category).ToHashSet();
        List<string> desc = new();
        foreach (var category in categories)
        {
            var values = descriptors.Where(x => x.Category == category)?.Select(x => x.Value);
            desc.Add(category + ": [" + String.Join(", ", values) + "]");
        }

        return String.Join(Environment.NewLine, desc);
    }

    /// <summary>Formats a list of race FormKeys as a bracketed, comma-separated list of display names.</summary>
    /// <param name="formKeys">Race FormKeys to format.</param>
    /// <param name="lk">Link cache for resolution.</param>
    /// <param name="patcherState">Patcher state (controls verbose naming detail).</param>
    /// <returns>e.g. "[Nord, Imperial, Snow Elf]".</returns>
    public static string GetRaceListLogStrings(IEnumerable<FormKey> formKeys, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        return "[" + String.Join(", ", formKeys.Select(x => GetRaceLogString(x, lk, patcherState))) + "]";
    }

    /// <summary>Races whose friendly display name differs from their record name, keyed by FormKey.</summary>
    /// <remarks>Keyed by <c>.FormKey</c>: the <c>Mutagen...FormKeys.SkyrimSE...Race.*</c> members are
    /// <c>FormLink</c>s, not <c>FormKey</c>s, so a direct <c>fk.Equals(formLink)</c> is always false (B57).</remarks>
    private static readonly Dictionary<FormKey, string> _specialCaseRaceLogNames = new()
    {
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey, "Afflicted" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey, "Astrid Race" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey, "Snow Elf" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.DLC1NordRace.FormKey, "Nord (Dawnguard)" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dragonborn.Race.DLC2MiraakRace.FormKey, "Nord (Miraak)" },
    };

    /// <summary>Returns the curated display name for a race whose friendly name differs from its record name, if one is defined.</summary>
    /// <param name="raceFormKey">The race FormKey to look up.</param>
    /// <param name="name">Receives the curated name when present.</param>
    /// <returns><c>true</c> if a curated special-case name exists for the race.</returns>
    public static bool TryGetSpecialCaseRaceLogName(FormKey raceFormKey, out string name)
    {
        return _specialCaseRaceLogNames.TryGetValue(raceFormKey, out name);
    }

    /// <summary>Resolves a single race FormKey to a friendly display name, with special cases for races whose display name differs from their record name.</summary>
    /// <param name="fk">The race FormKey.</param>
    /// <param name="lk">Link cache for resolution.</param>
    /// <param name="patcherState">Patcher state; when detailed-attribute verbosity is off, returns the raw FormKey string.</param>
    /// <returns>A display name (appending " Vampire" for vampire variants), or a fallback when not in the load order.</returns>
    public static string GetRaceLogString(FormKey fk, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        if (!patcherState.GeneralSettings.VerboseModeDetailedAttributes)
        {
            return fk.ToString();
        }

        // specific races whose display names aren't the same as their "real" names
        if (TryGetSpecialCaseRaceLogName(fk, out var specialCaseName))
        {
            return specialCaseName;
        }

        // general handling
        if (lk.TryResolve<IRaceGetter>(fk, out var raceGetter))
        {
            var edid = EditorIDHandler.GetEditorIDSafely(raceGetter);
            if (raceGetter.Name != null && !raceGetter.Name.ToString().IsNullOrWhitespace())
            {
                var name = raceGetter.Name.ToString();
                if (edid.Contains("Vampire", StringComparison.OrdinalIgnoreCase))
                {
                    name += " Vampire";
                }
                return name;
            }
            else
            {
                return edid;
            }
        }
        else
        {
            return "(Not Currently In Load Order)";
        }
    }

    /// <summary>Formats any major record as a readable string: its name, and (optionally) a qualifier with EditorID and FormKey.</summary>
    /// <param name="getter">The record to describe; may be null.</param>
    /// <param name="fullyQualified">When <c>true</c>, always include the EditorID/FormKey qualifier even if the record has a name.</param>
    /// <returns>"NULL" for a null record; otherwise a name and/or "EditorID | FormKey" string.</returns>
    public static string GetFormLogString(IMajorRecordGetter getter, bool fullyQualified = false)
    {
        if (getter == null)
        {
            return "NULL";
        }

        string str = string.Empty;

        if (getter is INamedGetter named && named.Name != null)
        {
            str += named.Name;
            if (!fullyQualified)
            {
                return str;
            }
        }

        string qual = string.Empty;

        if (getter.EditorID != null)
        {
            qual += getter.EditorID + " | ";
        }

        qual += getter.FormKey.ToString();

        if (str != string.Empty)
        {
            str += " (" + qual + ")";
        }
        else
        {
            str += qual;
        }

        return str;
    }
}
