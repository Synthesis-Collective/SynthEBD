using System.IO;

namespace SynthEBD;

/// <summary>
/// Loads, resolves, and saves the <see cref="BodyTypeRuleSet"/> files in <c>Body Type Rules\</c>.
///
/// <para><b>Active-file projection.</b> Every file is loaded into
/// <see cref="PatcherState.BodyTypeRuleSets"/>, but only the active file per body type feeds anything.
/// <see cref="ApplyActiveRuleSetViews"/> projects the active files into the two runtime views on
/// <see cref="Settings_OBody"/> (<see cref="Settings_OBody.BodySlideClassificationRules"/> and
/// <see cref="Settings_OBody.BodyTypeProfiles"/>), which is what the labeling menus load from and the
/// patcher reads. <see cref="SaveRuleSets"/> is the inverse: it folds the (VM-dumped) views back into
/// the active files and writes whatever changed.</para>
///
/// <para>The views hold deep copies, so a VM editing a view never aliases a rule-set object -- in
/// particular the object of a file that is no longer active after a Misc-menu switch.</para>
/// </summary>
public class SettingsIO_BodyTypeRules
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;

    public SettingsIO_BodyTypeRules(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }

    /// <summary>Loads every <c>*.json</c> in the rule folder. Unreadable files are logged and skipped
    /// (never deleted or overwritten -- a later save only writes files that loaded).</summary>
    public List<BodyTypeRuleSet> LoadRuleSets() => LoadRuleSets(_paths.BodyTypeRulesDirPath, _logger);

    /// <summary>Static core of <see cref="LoadRuleSets()"/> for tests and headless callers.</summary>
    public static List<BodyTypeRuleSet> LoadRuleSets(string directory, Logger? logger)
    {
        var result = new List<BodyTypeRuleSet>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return result;

        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                logger?.LogError("Body Type Rules: could not read " + path + ": " + ex.Message);
                continue;
            }

            var ruleSet = JSONhandler<BodyTypeRuleSet>.Deserialize(text, out bool ok, out string error);
            if (!ok || ruleSet == null)
            {
                logger?.LogError("Body Type Rules: could not parse " + path + ": " + error);
                continue;
            }
            if (string.IsNullOrWhiteSpace(ruleSet.BodyTypeName))
            {
                logger?.LogError("Body Type Rules: " + path + " has no BodyTypeName and was ignored.");
                continue;
            }

            ruleSet.FilePath = path;
            if (string.IsNullOrWhiteSpace(ruleSet.Name)) ruleSet.Name = Path.GetFileNameWithoutExtension(path);
            ruleSet.NormalizeBodyType();
            ruleSet.LastPersistedJson = text;
            result.Add(ruleSet);
        }

        logger?.LogMessage($"Body Type Rules: loaded {result.Count} file(s) from {directory}.");
        return result;
    }

    /// <summary>Every body type that has at least one rule file, in first-seen order.</summary>
    public static List<string> BodyTypesWithFiles(IEnumerable<BodyTypeRuleSet> ruleSets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (var rs in ruleSets ?? Enumerable.Empty<BodyTypeRuleSet>())
        {
            if (rs != null && !string.IsNullOrWhiteSpace(rs.BodyTypeName) && seen.Add(rs.BodyTypeName)) ordered.Add(rs.BodyTypeName);
        }
        return ordered;
    }

    /// <summary>The files targeting <paramref name="bodyType"/>, ordered by display name.</summary>
    public static List<BodyTypeRuleSet> FilesForBodyType(IEnumerable<BodyTypeRuleSet> ruleSets, string bodyType)
        => (ruleSets ?? Enumerable.Empty<BodyTypeRuleSet>())
            .Where(r => r != null && string.Equals(r.BodyTypeName, bodyType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Case-insensitive lookup into <see cref="Settings_OBody.SelectedRuleFileByBodyType"/>
    /// (the dictionary's comparer does not survive deserialization, so never index it directly).</summary>
    public static string GetSelectedFileName(Settings_OBody settings, string bodyType)
    {
        if (settings?.SelectedRuleFileByBodyType == null || string.IsNullOrEmpty(bodyType)) return "";
        foreach (var kv in settings.SelectedRuleFileByBodyType)
        {
            if (string.Equals(kv.Key, bodyType, StringComparison.OrdinalIgnoreCase)) return kv.Value ?? "";
        }
        return "";
    }

    /// <summary>Records <paramref name="fileName"/> as the active file for <paramref name="bodyType"/>,
    /// replacing any entry whose key differs only in case.</summary>
    public static void SetSelectedFileName(Settings_OBody settings, string bodyType, string fileName)
    {
        if (settings == null || string.IsNullOrEmpty(bodyType)) return;
        settings.SelectedRuleFileByBodyType ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in settings.SelectedRuleFileByBodyType.Keys.Where(k => string.Equals(k, bodyType, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            settings.SelectedRuleFileByBodyType.Remove(key);
        }
        if (!string.IsNullOrEmpty(fileName)) settings.SelectedRuleFileByBodyType[bodyType] = fileName;
    }

    /// <summary>The active file for <paramref name="bodyType"/>: the one named in
    /// <see cref="Settings_OBody.SelectedRuleFileByBodyType"/> if it still exists, else the first file
    /// for the body type by display name, else null.</summary>
    public static BodyTypeRuleSet? ResolveActive(Settings_OBody settings, IEnumerable<BodyTypeRuleSet> ruleSets, string bodyType)
    {
        var candidates = FilesForBodyType(ruleSets, bodyType);
        if (candidates.Count == 0) return null;
        var selected = GetSelectedFileName(settings, bodyType);
        if (!string.IsNullOrEmpty(selected))
        {
            var match = candidates.FirstOrDefault(c => string.Equals(c.FileName, selected, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return candidates[0];
    }

    /// <summary>Projects the active file of every body type into
    /// <see cref="Settings_OBody.BodySlideClassificationRules"/> and
    /// <see cref="Settings_OBody.BodyTypeProfiles"/> (deep copies). Also pins each body type's
    /// resolved active file into <see cref="Settings_OBody.SelectedRuleFileByBodyType"/> so the
    /// selection is explicit from here on.</summary>
    public static void ApplyActiveRuleSetViews(Settings_OBody settings, IEnumerable<BodyTypeRuleSet> ruleSets)
    {
        if (settings == null) return;
        settings.BodySlideClassificationRules = new Dictionary<string, SliderClassificationRulesByBodyType>(StringComparer.OrdinalIgnoreCase);
        settings.BodyTypeProfiles = new List<BodyTypeProfile>();

        foreach (var bodyType in BodyTypesWithFiles(ruleSets))
        {
            var active = ResolveActive(settings, ruleSets, bodyType);
            if (active == null) continue;
            SetSelectedFileName(settings, bodyType, active.FileName);

            var sliderRules = JSONhandler<SliderClassificationRulesByBodyType>.CloneViaJSON(active.SliderRules ?? new SliderClassificationRulesByBodyType());
            sliderRules.BodyTypeGroup = active.BodyTypeName;
            settings.BodySlideClassificationRules[active.BodyTypeName] = sliderRules;

            if (active.MeasurementProfile != null)
            {
                var profile = JSONhandler<BodyTypeProfile>.CloneViaJSON(active.MeasurementProfile);
                profile.BodyTypeName = active.BodyTypeName;
                settings.BodyTypeProfiles.Add(profile);
            }
        }
    }

    /// <summary>Folds the runtime views back into the active files and writes every file whose
    /// serialized content changed. A body type with authored content in the views but no file yet
    /// gets a new file named after the body type. Descriptor definitions are refreshed from
    /// <see cref="Settings_OBody.TemplateDescriptors"/> first.</summary>
    public bool SaveRuleSets(Settings_OBody settings, List<BodyTypeRuleSet> ruleSets, out string errors)
        => SaveRuleSets(settings, ruleSets, _paths.BodyTypeRulesDirPath, _logger, out errors);

    /// <summary>Static core of <see cref="SaveRuleSets(Settings_OBody, List{BodyTypeRuleSet}, out string)"/>.</summary>
    public static bool SaveRuleSets(Settings_OBody settings, List<BodyTypeRuleSet> ruleSets, string directory, Logger? logger, out string errors)
    {
        errors = "";
        if (settings == null || ruleSets == null) return true;

        FoldViewsIntoActiveFiles(settings, ruleSets, directory);

        bool allOk = true;
        foreach (var ruleSet in ruleSets)
        {
            ruleSet.NormalizeBodyType();
            ruleSet.RefreshDescriptorDefinitions(settings.TemplateDescriptors);

            string json = JSONhandler<BodyTypeRuleSet>.Serialize(ruleSet, out bool ok, out string serializeError);
            if (!ok)
            {
                allOk = false;
                errors += "Could not serialize body type rule file " + ruleSet.Name + ": " + serializeError + Environment.NewLine;
                continue;
            }
            if (string.Equals(json, ruleSet.LastPersistedJson, StringComparison.Ordinal)) continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ruleSet.FilePath)!);
                File.WriteAllText(ruleSet.FilePath, json);
                ruleSet.LastPersistedJson = json;
                logger?.LogMessage("Body Type Rules: saved " + ruleSet.FilePath);
            }
            catch (Exception ex)
            {
                allOk = false;
                errors += "Could not write body type rule file " + ruleSet.FilePath + ": " + ex.Message + Environment.NewLine;
            }
        }
        return allOk;
    }

    /// <summary>The fold step of <see cref="SaveRuleSets(Settings_OBody, List{BodyTypeRuleSet}, string, Logger?, out string)"/>,
    /// separated for testing. For every body type present in the views or in the files: the active
    /// file's slider rules and profile are replaced by the views' (an absent view means "none" -- the
    /// views are complete because they were projected from, and dumped over, the active files).</summary>
    public static void FoldViewsIntoActiveFiles(Settings_OBody settings, List<BodyTypeRuleSet> ruleSets, string directory)
    {
        var sliderViews = new Dictionary<string, SliderClassificationRulesByBodyType>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in settings.BodySlideClassificationRules ?? new())
        {
            if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null) sliderViews[kv.Key] = kv.Value;
        }
        var profileViews = new Dictionary<string, BodyTypeProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in settings.BodyTypeProfiles ?? new())
        {
            if (p != null && !string.IsNullOrWhiteSpace(p.BodyTypeName) && !profileViews.ContainsKey(p.BodyTypeName)) profileViews[p.BodyTypeName] = p;
        }

        var bodyTypes = new List<string>(BodyTypesWithFiles(ruleSets));
        foreach (var bt in sliderViews.Keys.Concat(profileViews.Keys))
        {
            if (!bodyTypes.Contains(bt, StringComparer.OrdinalIgnoreCase)) bodyTypes.Add(bt);
        }

        foreach (var bodyType in bodyTypes)
        {
            sliderViews.TryGetValue(bodyType, out var sliderView);
            profileViews.TryGetValue(bodyType, out var profileView);

            var active = ResolveActive(settings, ruleSets, bodyType);
            if (active == null)
            {
                // No file yet: only create one for authored content. The Label by Sliders menu dumps
                // an empty skeleton for every body type with installed presets, which must not spawn
                // a file per installed body.
                if (!BodyTypeRuleSet.HasSliderContent(sliderView) && profileView == null) continue;
                active = new BodyTypeRuleSet
                {
                    Name = bodyType,
                    BodyTypeName = bodyType,
                    FilePath = UniqueFilePath(directory, bodyType, ruleSets),
                };
                ruleSets.Add(active);
                SetSelectedFileName(settings, bodyType, active.FileName);
            }

            active.SliderRules = sliderView != null
                ? JSONhandler<SliderClassificationRulesByBodyType>.CloneViaJSON(sliderView)
                : new SliderClassificationRulesByBodyType();
            active.MeasurementProfile = profileView != null
                ? JSONhandler<BodyTypeProfile>.CloneViaJSON(profileView)
                : null;
            active.NormalizeBodyType();
        }
    }

    /// <summary>A path in <paramref name="directory"/> for a new file named after
    /// <paramref name="displayName"/> (invalid characters replaced), suffixed " (2)", " (3)", ... to
    /// avoid both existing files on disk and files already claimed in <paramref name="ruleSets"/>.</summary>
    public static string UniqueFilePath(string directory, string displayName, IEnumerable<BodyTypeRuleSet>? ruleSets)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = new string((displayName ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (stem.Length == 0) stem = "Body Type Rules";

        var claimed = new HashSet<string>(
            (ruleSets ?? Enumerable.Empty<BodyTypeRuleSet>()).Select(r => r.FilePath ?? ""),
            StringComparer.OrdinalIgnoreCase);
        string candidate = Path.Combine(directory, stem + ".json");
        int suffix = 2;
        while (claimed.Contains(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(directory, stem + " (" + suffix + ").json");
            suffix++;
        }
        return candidate;
    }

    /// <summary>A new, not-yet-saved copy of <paramref name="source"/> named "&lt;Name&gt; (copy)", with
    /// its own file path. The caller adds it to <see cref="PatcherState.BodyTypeRuleSets"/>; it is written
    /// on the next save. The measurement profile keeps its Id and Name, so it shares the source's
    /// measurement cache (the cache validates itself against the definitions, so a later divergence is
    /// detected rather than trusted).</summary>
    public static BodyTypeRuleSet Duplicate(BodyTypeRuleSet source, string directory, IEnumerable<BodyTypeRuleSet>? ruleSets)
    {
        var copy = JSONhandler<BodyTypeRuleSet>.CloneViaJSON(source);
        var existingNames = new HashSet<string>((ruleSets ?? Enumerable.Empty<BodyTypeRuleSet>()).Select(r => r.Name ?? ""), StringComparer.OrdinalIgnoreCase);
        string baseName = (string.IsNullOrWhiteSpace(source.Name) ? source.BodyTypeName : source.Name.Trim()) + " (copy)";
        copy.Name = baseName;
        int suffix = 2;
        while (existingNames.Contains(copy.Name))
        {
            copy.Name = baseName + " " + suffix;
            suffix++;
        }
        copy.FilePath = UniqueFilePath(directory, copy.Name, ruleSets);
        copy.LastPersistedJson = "";
        copy.NormalizeBodyType();
        return copy;
    }
}
