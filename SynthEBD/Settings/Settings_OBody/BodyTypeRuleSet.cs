using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

namespace SynthEBD;

/// <summary>
/// One shareable body-type rule file (<c>Body Type Rules\&lt;name&gt;.json</c>): the Label by Sliders
/// rules and the Label by Measurements profile for a single body type, stored together.
///
/// <para><b>Why the two travel in one file.</b> Measurement rules can gate on descriptors the slider
/// rules produce (<see cref="MeasurementConditionKind.DescriptorRef"/>), and the two menus share one
/// per-category default (<see cref="DescriptorDefaultSynchronizer"/>). Splitting them would let a user
/// pair a measurement profile with slider rules it was never tuned against.</para>
///
/// <para><b>Several files per body type.</b> Any number of files may target the same
/// <see cref="BodyTypeName"/>; the OBody Misc menu picks the active one per body type
/// (<see cref="Settings_OBody.SelectedRuleFileByBodyType"/>). Only the active file feeds the
/// labeling menus and the patcher -- see <see cref="SettingsIO_BodyTypeRules"/>.</para>
///
/// <para><b>What is deliberately not in here.</b> The profile's
/// <see cref="BodyTypeProfile.PresetAnnotations"/> (the user's worklist verdicts) and
/// <see cref="BodyTypeProfile.AnnotatorPrefs"/> (UI state) are personal, so they are
/// <c>[JsonIgnore]</c> on the profile and persist in <c>OBodySettings.json</c> instead, keyed by body
/// type. Switching the active file therefore never changes them.</para>
/// </summary>
[DebuggerDisplay("{Name} ({BodyTypeName}) -- {FileName}")]
public class BodyTypeRuleSet
{
    /// <summary>Content version of this file, bumped by its author when publishing an update. Used to
    /// detect that a shipped default is newer than a user's installed copy.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Display name shown in the Misc menu's per-body-type combobox. Independent of the file
    /// name, so two downloaded files that happen to share a file name can still be told apart.</summary>
    public string Name { get; set; } = "";

    /// <summary>Body type this file targets. Matches <see cref="BodyTypeRegistryEntry.Name"/> and the
    /// <see cref="BodySlideSetting.SliderGroup"/> the classifier assigns to presets of that body.</summary>
    public string BodyTypeName { get; set; } = "";

    /// <summary>Label by Sliders rules. <see cref="SliderClassificationRulesByBodyType.BodyTypeGroup"/>
    /// is normalized to <see cref="BodyTypeName"/> on load and save.</summary>
    public SliderClassificationRulesByBodyType SliderRules { get; set; } = new();

    /// <summary>Label by Measurements profile, or null when this file carries slider rules only.
    /// <see cref="BodyTypeProfile.BodyTypeName"/> is normalized to <see cref="BodyTypeName"/>.</summary>
    public BodyTypeProfile? MeasurementProfile { get; set; }

    /// <summary>Definitions of every descriptor this file's rules reference, written on save from the
    /// author's settings. On load, any definition the recipient's settings lack is offered for import;
    /// an existing definition of the same Category/Value is never overwritten. Distribution rules
    /// (<see cref="BodyShapeDescriptor.AssociatedRules"/>) are stripped: they are the author's local
    /// distribution policy, not part of what the label means.</summary>
    [JsonConverter(typeof(BodyShapeDescriptorShellListConverter))]
    public List<BodyShapeDescriptorShell> DescriptorDefinitions { get; set; } = new();

    /// <summary>Full path the file was loaded from / will be written to. Runtime only.</summary>
    [JsonIgnore]
    public string FilePath { get; set; } = "";

    /// <summary>File name component of <see cref="FilePath"/>; the key persisted in
    /// <see cref="Settings_OBody.SelectedRuleFileByBodyType"/>.</summary>
    [JsonIgnore]
    public string FileName => string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath);

    /// <summary>Serialized text last read from or written to disk. Lets the save pass skip files
    /// whose content has not changed, so an idle save doesn't touch every file's timestamp.</summary>
    [JsonIgnore]
    public string LastPersistedJson { get; set; } = "";

    /// <summary>True when the slider rules carry any rule or default, or a measurement profile exists.</summary>
    public bool HasContent() => HasSliderContent(SliderRules) || MeasurementProfile != null;

    /// <summary>True when <paramref name="rules"/> carries at least one rule or category default. The
    /// Label by Sliders menu dumps an empty 17-category skeleton for every body type that has presets
    /// installed; those must not be mistaken for authored content.</summary>
    public static bool HasSliderContent(SliderClassificationRulesByBodyType? rules)
    {
        if (rules?.DescriptorClassifiers == null) return false;
        return rules.DescriptorClassifiers.Any(c => c != null
            && (!string.IsNullOrEmpty(c.DefaultDescriptorValue) || (c.RuleList?.Count ?? 0) > 0));
    }

    /// <summary>Forces the nested body-type fields to agree with <see cref="BodyTypeName"/>.</summary>
    public void NormalizeBodyType()
    {
        SliderRules ??= new SliderClassificationRulesByBodyType();
        SliderRules.DescriptorClassifiers ??= new();
        SliderRules.BodyTypeGroup = BodyTypeName;
        if (MeasurementProfile != null) MeasurementProfile.BodyTypeName = BodyTypeName;
    }

    /// <summary>Every (Category, Value) this file's rules can emit or test: slider rule values and
    /// defaults, measurement rule outputs, DescriptorRef targets, and measurement defaults.
    /// Entries with an empty Value (a category mentioned without a specific value) are included so
    /// the category itself is carried.</summary>
    public HashSet<(string Category, string Value)> CollectReferencedDescriptors()
    {
        var result = new HashSet<(string, string)>();
        void Add(string? category, string? value)
        {
            if (string.IsNullOrWhiteSpace(category)) return;
            result.Add((category, value ?? ""));
        }

        foreach (var classifier in SliderRules?.DescriptorClassifiers ?? new())
        {
            // An empty skeleton category (no default, no rules) references nothing, so its
            // definition is not carried.
            if (classifier == null) continue;
            if (!string.IsNullOrEmpty(classifier.DefaultDescriptorValue))
            {
                Add(classifier.DescriptorCategory, classifier.DefaultDescriptorValue);
            }
            foreach (var rule in classifier.RuleList ?? new())
            {
                if (rule == null) continue;
                Add(classifier.DescriptorCategory, rule.SelectedDescriptorValue);
            }
        }

        if (MeasurementProfile != null)
        {
            foreach (var rule in MeasurementProfile.Rules ?? new())
            {
                if (rule == null) continue;
                Add(rule.Descriptor?.Category, rule.Descriptor?.Value);
                foreach (var group in rule.GroupsORlogic ?? new())
                {
                    foreach (var condition in group?.ConditionsANDlogic ?? new())
                    {
                        if (condition?.Kind == MeasurementConditionKind.DescriptorRef)
                        {
                            Add(condition.RefCategory, condition.RefValue);
                        }
                    }
                }
            }
            foreach (var kv in MeasurementProfile.DefaultDescriptorValuesByCategory ?? new())
            {
                Add(kv.Key, kv.Value);
            }
        }
        return result;
    }

    /// <summary>Rebuilds <see cref="DescriptorDefinitions"/> from <paramref name="templateDescriptors"/>
    /// (the author's descriptor catalog), keeping only what <see cref="CollectReferencedDescriptors"/>
    /// reports. References absent from the catalog are simply not carried -- there is no definition to
    /// write.</summary>
    public void RefreshDescriptorDefinitions(IEnumerable<BodyShapeDescriptorShell>? templateDescriptors)
    {
        var referenced = CollectReferencedDescriptors();
        var referencedCategories = new HashSet<string>(referenced.Select(r => r.Category), StringComparer.Ordinal);
        var output = new List<BodyShapeDescriptorShell>();
        foreach (var shell in templateDescriptors ?? Enumerable.Empty<BodyShapeDescriptorShell>())
        {
            if (shell == null || !referencedCategories.Contains(shell.Category)) continue;
            var copy = new BodyShapeDescriptorShell
            {
                Category = shell.Category,
                CategoryDescription = shell.CategoryDescription ?? "",
                IsRulesOnly = shell.IsRulesOnly,
            };
            foreach (var descriptor in shell.Descriptors ?? new())
            {
                var value = descriptor?.ID?.Value;
                if (string.IsNullOrEmpty(value) || !referenced.Contains((shell.Category, value))) continue;
                copy.Descriptors.Add(StripToDefinition(shell.Category, descriptor!));
            }
            output.Add(copy);
        }
        DescriptorDefinitions = output;
    }

    /// <summary>Returns the definitions in <see cref="DescriptorDefinitions"/> that
    /// <paramref name="templateDescriptors"/> lacks, grouped by category. A category the user already
    /// has contributes only its missing values; a missing category comes with its description and
    /// rules-only flag. Keys in <paramref name="declinedKeys"/> (see <see cref="DeclineKey"/>) are
    /// omitted, so a user who skipped an import isn't asked again.</summary>
    public List<BodyShapeDescriptorShell> FindMissingDescriptorDefinitions(
        IEnumerable<BodyShapeDescriptorShell>? templateDescriptors,
        ICollection<string>? declinedKeys = null)
    {
        var existing = (templateDescriptors ?? Enumerable.Empty<BodyShapeDescriptorShell>())
            .Where(s => s != null)
            .GroupBy(s => s.Category ?? "", StringComparer.Ordinal)
            .ToDictionary(g => g.Key,
                g => new HashSet<string>(g.SelectMany(s => s.Descriptors ?? new()).Select(d => d?.ID?.Value ?? ""), StringComparer.Ordinal),
                StringComparer.Ordinal);

        var missing = new List<BodyShapeDescriptorShell>();
        foreach (var shell in DescriptorDefinitions ?? new())
        {
            if (shell == null || string.IsNullOrWhiteSpace(shell.Category)) continue;
            bool categoryExists = existing.TryGetValue(shell.Category, out var existingValues);
            // An existing category contributes only its missing values: its description and flags are
            // the user's and are not offered (a merge could otherwise fill an empty description).
            var missingShell = new BodyShapeDescriptorShell
            {
                Category = shell.Category,
                CategoryDescription = categoryExists ? "" : shell.CategoryDescription ?? "",
                IsRulesOnly = !categoryExists && shell.IsRulesOnly,
            };
            foreach (var descriptor in shell.Descriptors ?? new())
            {
                var value = descriptor?.ID?.Value;
                if (string.IsNullOrEmpty(value)) continue;
                if (existingValues != null && existingValues.Contains(value)) continue;
                if (declinedKeys != null && declinedKeys.Contains(DeclineKey(FileName, shell.Category, value))) continue;
                missingShell.Descriptors.Add(StripToDefinition(shell.Category, descriptor!));
            }
            if (missingShell.Descriptors.Count > 0) missing.Add(missingShell);
        }
        return missing;
    }

    /// <summary>Adds <paramref name="missing"/> (from <see cref="FindMissingDescriptorDefinitions"/>) to
    /// <paramref name="templateDescriptors"/>. New categories are appended whole; existing categories
    /// gain only the values they lack, and their description and flags are left untouched.</summary>
    public static int MergeMissingDescriptorDefinitions(List<BodyShapeDescriptorShell> templateDescriptors, IEnumerable<BodyShapeDescriptorShell> missing)
    {
        int added = 0;
        foreach (var shell in missing ?? Enumerable.Empty<BodyShapeDescriptorShell>())
        {
            if (shell == null) continue;
            var target = templateDescriptors.FirstOrDefault(s => string.Equals(s?.Category, shell.Category, StringComparison.Ordinal));
            if (target == null)
            {
                target = new BodyShapeDescriptorShell
                {
                    Category = shell.Category,
                    CategoryDescription = shell.CategoryDescription ?? "",
                    IsRulesOnly = shell.IsRulesOnly,
                };
                templateDescriptors.Add(target);
            }
            foreach (var descriptor in shell.Descriptors ?? new())
            {
                var value = descriptor?.ID?.Value;
                if (string.IsNullOrEmpty(value)) continue;
                if (target.Descriptors.Any(d => string.Equals(d?.ID?.Value, value, StringComparison.Ordinal))) continue;
                target.Descriptors.Add(StripToDefinition(target.Category, descriptor!));
                added++;
            }
        }
        return added;
    }

    /// <summary>Key recorded in <see cref="Settings_OBody.DeclinedDescriptorImports"/> when the user
    /// skips importing a descriptor from a rule file.</summary>
    public static string DeclineKey(string fileName, string category, string value)
        => (fileName ?? "") + "|" + BodyShapeDescriptor.LabelSignature.ToSignatureString(category, value);

    private static BodyShapeDescriptor StripToDefinition(string category, BodyShapeDescriptor source) => new()
    {
        ID = new BodyShapeDescriptor.LabelSignature { Category = category, Value = source.ID?.Value ?? "" },
        ValueDescription = source.ValueDescription,
    };
}
