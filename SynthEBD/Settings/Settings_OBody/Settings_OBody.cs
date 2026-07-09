using Mutagen.Bethesda.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Xml.Linq;

namespace SynthEBD;

public enum AutoBodySelectionMode
{
    INI,
    JSON
}

public enum OBodySelectionMode
{
    Native,
    Script
}

public class Settings_OBody
{
    public int SchemaVersion { get; set; } = 0; // bumped to 1 once BodySlideSettingMigrator has run
    public List<int> DefaultWeightSlots { get; set; } = new() { 0, 25, 50, 75, 100 };
    public List<BodySlideSetting> BodySlidesMale { get; set; } = new();
    public List<BodySlideSetting> BodySlidesFemale { get; set; } = new();
    // Persistence shape changed in 2026: previously a flat HashSet<BodyShapeDescriptor> where
    // CategoryDescription was duplicated across every value of the same category. Now grouped
    // into shells with one CategoryDescription per category. The JsonConverter handles the
    // legacy flat shape on load, so old OBodySettings.json files keep loading.
    [JsonConverter(typeof(BodyShapeDescriptorShellListConverter))]
    public List<BodyShapeDescriptorShell> TemplateDescriptors { get; set; } = new()
    {
        new BodyShapeDescriptorShell
        {
            Category = "Build",
            Descriptors = new()
            {
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Slight" } },
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Medium" } },
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Curvy" } },
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Chubby" } },
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Exaggerated" } },
                new BodyShapeDescriptor { ID = new() { Category = "Build", Value = "Powerful" } },
            },
        },
        new BodyShapeDescriptorShell
        {
            Category = "Chest",
            Descriptors = new()
            {
                new BodyShapeDescriptor { ID = new() { Category = "Chest", Value = "Busty" } },
                new BodyShapeDescriptor { ID = new() { Category = "Chest", Value = "Medium" } },
                new BodyShapeDescriptor { ID = new() { Category = "Chest", Value = "Petite" } },
            },
        },
    };

    public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();

    /// <summary>
    /// User overrides for SliderCategories.xml lookup, keyed by body-type name (e.g. "CBBE").
    /// When set and the file exists, takes precedence over the shipped fallback in InternalData/SliderCatalogs/.
    /// Stage 4: feeds <see cref="BodySlideGroupClassifier"/>.
    /// </summary>
    public Dictionary<string, string> SliderCatalogOverridePaths { get; set; } = new();

    /// <summary>
    /// User-editable family relationships between body types. Key is the canonical body type the user
    /// prefers; value is the set of alias names that should resolve to that key during classification.
    /// Example: {"CBBE": ["3BA", "3BBB", "CBAdvanced"]}.
    /// Stage 4: consumed by <see cref="BodySlideGroupClassifier"/>.
    /// </summary>
    public Dictionary<string, HashSet<string>> BodyTypeFamilyCompatibility { get; set; } = new();

    /// <summary>
    /// User-extensible catalog of parent body types (e.g. CBBE, CBBE 3BA, BHUNP, HIMBO). Each entry
    /// describes identity fingerprints (files whose existence proves the body is installed) and
    /// ShapeData subfolders (whose OSD/BSD files define the body's slider set at load time).
    /// Shipped defaults are merged in from InternalData/SliderCatalogs/BodyTypeRegistry.json on
    /// first load; user edits set <see cref="BodyTypeRegistryEntry.IsUserDefined"/> so they survive
    /// future shipped-defaults churn. Feeds <see cref="BodySlideGroupClassifier"/> (slider-only
    /// subset match) and <see cref="VM_CharacterViewer"/> OSD linkage.
    /// </summary>
    public List<BodyTypeRegistryEntry> BodyTypeRegistry { get; set; } = new();

    /// <summary>
    /// User-authored BodySlide Classifier profiles (Phase 3 of the classifier pipeline). Each
    /// profile pairs a body type from <see cref="BodyTypeRegistry"/> with a set of key vertices,
    /// measurement definitions, and classification rules. Authored once per body type in the
    /// Body Type Profile Editor and reused across every preset that shares the body's topology.
    /// Empty list = no profiles authored yet (safe default; classifier simply no-ops).
    /// </summary>
    public List<BodyTypeProfile> BodyTypeProfiles { get; set; } = new();

    public bool bUseVerboseScripts { get; set; } = false;
    public OBodySelectionMode OBodySelectionMode { get; set; } = OBodySelectionMode.Native;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;
    public Dictionary<string, SliderClassificationRulesByBodyType> BodySlideClassificationRules { get; set; } = new(); // key is Slider Group (e.g. CBBE, UNP, etc)
    public bool AutoApplyMissingAnnotations { get; set; } = true;

    /// <summary>
    /// Conflict policy for <see cref="DescriptorDefaultSynchronizer"/>'s load-time reconciliation of
    /// the per-category default descriptor shared between Label by Sliders
    /// (<see cref="SliderClassificationRulesByBodyType"/>) and Label by Measurements
    /// (<see cref="BodyTypeProfile.DefaultDescriptorValuesByCategory"/>): when saved settings carry
    /// two different non-empty defaults for the same (body type, category), true means the slider
    /// side's value wins; false (default) means the measurement side's value wins. Live edits always
    /// propagate both ways regardless of this flag — it only breaks pre-existing disagreements.
    /// </summary>
    public bool PreferSliderDefaultsOnConflict { get; set; } = false;
    public bool OBodyEnableMultipleAssignments { get; set; } = false;

    /// <summary>
    /// Per-weight-slot preview NPCs for the 3D Character Viewer hosted inside the BodySlide
    /// detail pane. New field; no migration required (defaults to empty, populated lazily).
    /// </summary>
    public OBodyPreviewNpcSettings PreviewNpcs { get; set; } = new();

    [JsonIgnore]
    public HashSet<string> CurrentlyExistingBodySlides { get; set; } = new();

    public void ImportBodySlides(List<BodyShapeDescriptorShell> templateDescriptors, SettingsIO_OBody oBodyIO, string gameDataFolder, Logger logger, BodySlideGroupClassifier classifier = null)
    {
        logger.LogStartupEventStart("Detecting currently installed BodySlides");

        // Gender lookup for the <Group>-tag fallback below. Derived inline from the registry --
        // there is no separate Male/FemaleSliderGroups list anymore. First entry wins on duplicate names.
        var registryGenderByName = new Dictionary<string, Gender>(StringComparer.OrdinalIgnoreCase);
        if (BodyTypeRegistry != null)
        {
            foreach (var entry in BodyTypeRegistry)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                if (!registryGenderByName.ContainsKey(entry.Name))
                {
                    registryGenderByName[entry.Name] = entry.Gender;
                }
            }
        }

        var defaultAnnotationDict = oBodyIO.LoadDefaultBodySlideAnnotation();

        CurrentlyExistingBodySlides.Clear();
        List<BodySlideSetting> currentBodySlides = new List<BodySlideSetting>();
        string loadFolder = System.IO.Path.Join(gameDataFolder, "CalienteTools\\BodySlide\\SliderPresets");
        if (System.IO.Directory.Exists(loadFolder))
        {
            var xmlFilePaths = System.IO.Directory.GetFiles(loadFolder, "*.xml");
            foreach (var xmlFilePath in xmlFilePaths)
            {
                try
                {
                    XDocument presetFile = XDocument.Load(xmlFilePath);
                    var presets = presetFile.Element("SliderPresets");
                    if (presets == null) { continue; }

                    foreach (var preset in presets.Elements())
                    {
                        var presetName = preset.Attribute("name").Value.ToString();
                        CurrentlyExistingBodySlides.Add(presetName);

                        // Slider-only classification (no preset-author metadata).
                        var presetSliderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ss in preset.Elements("SetSlider"))
                        {
                            var sn = ss.Attribute("name");
                            if (sn != null && !string.IsNullOrEmpty(sn.Value)) presetSliderNames.Add(sn.Value);
                        }

                        BodySlideClassification classification = classifier?.Classify(presetName, presetSliderNames);
                        string groupName = classification?.BodyType ?? "Unknown";
                        Gender gender = classification?.Gender ?? Gender.Female;

                        // Last-ditch <Group>-tag fallback: only used to refine *gender* when the
                        // slider catalog couldn't host the preset (so groupName stayed "Unknown").
                        // The body-type name from these tags is intentionally discarded -- preset
                        // authors set them sloppily and we can no longer trust them for routing.
                        if (groupName == "Unknown")
                        {
                            foreach (var group in preset.Elements("Group"))
                            {
                                var tagName = group.Attribute("name")?.Value;
                                if (string.IsNullOrEmpty(tagName)) continue;
                                if (registryGenderByName.TryGetValue(tagName, out var registryGender))
                                {
                                    gender = registryGender;
                                    break;
                                }
                            }
                        }

                        currentBodySlides = gender == Gender.Male ? BodySlidesMale : BodySlidesFemale;

                        BodySlideSetting currentPreset = currentBodySlides.FirstOrDefault(x => x.ReferencedBodySlide == presetName);

                        if (currentPreset == null)
                        {
                            BodySlideSetting newPreset = new BodySlideSetting();
                            newPreset.Label = presetName;
                            newPreset.ReferencedBodySlide = presetName;

                            if (newPreset.Label.Contains("Zero for OBody", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Zeroed Sliders", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Clothes", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Clothed", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Clothing", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Outfit", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Armor", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Armour", StringComparison.OrdinalIgnoreCase) ||
                                newPreset.Label.Contains("Refit ", StringComparison.OrdinalIgnoreCase))
                            {
                                newPreset.AllowRandom = false;
                                newPreset.HideInMenu = true;
                            }

                            if (defaultAnnotationDict.ContainsKey(presetName))
                            {
                                foreach (var annotation in defaultAnnotationDict[presetName])
                                {
                                    // Flatten() yields one BodyShapeDescriptor per (Category, Value)
                                    // across the new shell-grouped collection. The .ID.ToString()
                                    // comparison stays unchanged — the descriptor still carries the
                                    // full Category+Value pair in its ID.
                                    var descriptor = templateDescriptors.Flatten().FirstOrDefault(x => x.ID.ToString().Equals(annotation, StringComparison.OrdinalIgnoreCase));
                                    if (descriptor != null)
                                    {
                                        // Default CSV-shipped annotations are treated as Library-sourced (Tier 1).
                                        newPreset.AddDescriptorToAllSlots(new AnnotatedDescriptorSignature(descriptor.ID, BodyShapeAnnotationSource.Library));
                                    }
                                }
                            }

                            currentBodySlides.Add(newPreset);
                            currentPreset = newPreset;
                        }

                        currentPreset.SliderGroup = groupName;

                        var sliders = preset.Elements("SetSlider");
                        foreach (var slider in sliders)
                        {
                            var sliderName = slider.Attribute("name");
                            var size = slider.Attribute("size");
                            var value = slider.Attribute("value");

                            if (sliderName != null && size != null && value != null && int.TryParse(value.Value, out int iValue))
                            {
                                BodySlideSlider currentSlider;
                                if (currentPreset.SliderValues.ContainsKey(sliderName.Value))
                                {
                                    currentSlider = currentPreset.SliderValues[sliderName.Value];
                                }
                                else
                                {
                                    currentSlider = new() { SliderName = sliderName.Value };
                                    currentPreset.SliderValues.Add(sliderName.Value, currentSlider);
                                }
                                
                                if (size.Value.Equals("big", StringComparison.OrdinalIgnoreCase))
                                {
                                    currentSlider.Big = iValue;
                                }
                                else if (size.Value.Equals("small", StringComparison.OrdinalIgnoreCase))
                                {
                                    currentSlider.Small = iValue;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    logger.LogError("Warning: failed to read BodySlide XML file: " + xmlFilePath + ". This file may be corrupted or incorrectly formatted.");
                }
            }
        }
        logger.LogStartupEventEnd("Detecting currently installed BodySlides");
    }
}

[DebuggerDisplay("{Label}")]
public class BodySlideSetting : IProbabilityWeighted, IBodyShapeRuleCandidate
{
    public string Label { get; set; } = "";
    public string ReferencedBodySlide { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>
    /// Per-weight descriptors. Keys are integer weight slots (0-100). Default install slots are 0/25/50/75/100.
    /// Source of truth for descriptors after schema v1.
    /// </summary>
    public Dictionary<int, HashSet<AnnotatedDescriptorSignature>> BodyShapeDescriptorsByWeight { get; set; }
        = new()
        {
            { 0, new() },
            { 25, new() },
            { 50, new() },
            { 75, new() },
            { 100, new() },
        };

    /// <summary>
    /// Default weight slots the user has explicitly removed from this preset.
    /// Tracked so the migrator/UI can preserve the deletion across reloads.
    /// </summary>
    public HashSet<int> RemovedDefaultWeightSlots { get; set; } = new();

    /// <summary>
    /// Legacy backing field. Captured by Newtonsoft when reading pre-v1 settings files;
    /// the migrator drains this into <see cref="BodyShapeDescriptorsByWeight"/>.
    /// Marked NeverSerialize so freshly written files only contain the new shape.
    /// </summary>
    [JsonProperty("BodyShapeDescriptors", NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public HashSet<AnnotatedDescriptorSignature> LegacyBodyShapeDescriptors { get; set; } = null;

    public bool ShouldSerializeLegacyBodyShapeDescriptors() => false;

    public HashSet<FormKey> AllowedRaces { get; set; } = new();
    public HashSet<FormKey> DisallowedRaces { get; set; } = new();
    public HashSet<string> AllowedRaceGroupings { get; set; } = new();
    public HashSet<string> DisallowedRaceGroupings { get; set; } = new();
    public HashSet<NPCAttribute> AllowedAttributes { get; set; } = new(); // keeping as array to allow deserialization of original zEBD settings files
    public HashSet<NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public bool AllowRandom { get; set; } = true;
    public double ProbabilityWeighting { get; set; } = 1;
    public List<AttributeWeightModifier> ProbabilityWeightModifiers { get; set; } = new();
    public bool ShouldSerializeProbabilityWeightModifiers() => ProbabilityWeightModifiers.Count > 0;
    public NPCWeightRange WeightRange { get; set; } = new();
    public bool HideInMenu { get; set; } = false;
    [JsonIgnore]
    public BodyShapeAnnotationState AnnotationState { get; set; } = BodyShapeAnnotationState.None;

    // Per-NPC ForceIf match counts live in NPCInfo.ForceIfMatches (R19), NOT here: presets are shared
    // settings-owned objects, so per-NPC scratch on them would block parallel selection.

    /// <summary>BodySlide presets validate only the descriptors annotated at the NPC's weight slot, so per-weight presets aren't spuriously rejected by rules that only apply to the other slot.</summary>
    public HashSet<BodyShapeDescriptor.LabelSignature> GetDescriptorsForValidation(float npcWeight) => new(PerWeightDescriptorLookup.GetDescriptorsForWeight(this, npcWeight));

    [JsonIgnore]
    public string SliderGroup { get; set; }

    /// <summary>
    /// True when this preset's sliders matched a Body Type Registry entry at classification time
    /// (<see cref="SliderGroup"/> holds a body type other than the "Unknown" sentinel). Presets
    /// without a detected body type are excluded from distribution by RunPatcher and skipped by
    /// the pre-run unique-label validation.
    /// </summary>
    [JsonIgnore]
    public bool HasDetectedBodyType => !string.IsNullOrWhiteSpace(SliderGroup)
        && !string.Equals(SliderGroup, "Unknown", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public Dictionary<string, BodySlideSlider> SliderValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AnnotatedDescriptorSignature: BodyShapeDescriptor.LabelSignature
{
    public AnnotatedDescriptorSignature(BodyShapeDescriptor.LabelSignature template)
    {
        Category = template.Category;
        Value = template.Value;
    }
    public AnnotatedDescriptorSignature(BodyShapeDescriptor.LabelSignature template, BodyShapeAnnotationState annotationState)
    {
        Category = template.Category;
        Value = template.Value;
        AnnotationState = annotationState;
        Source = AnnotationStateToSource(annotationState);
    }

    public AnnotatedDescriptorSignature(BodyShapeDescriptor.LabelSignature template, BodyShapeAnnotationSource source)
    {
        Category = template.Category;
        Value = template.Value;
        Source = source;
        AnnotationState = SourceToAnnotationState(source);
    }

    [JsonConstructor]
    public AnnotatedDescriptorSignature([JsonProperty("Category")] string category, [JsonProperty("Value")] string value)
    {
        Category = category;
        Value = value;
        AnnotationState = BodyShapeAnnotationState.Manual;
        Source = BodyShapeAnnotationSource.Manual;
    }

    /// <summary>
    /// Annotation provenance for this entry. Persisted alongside Category/Value so the UI can
    /// surface where each (weight, descriptor) pair came from.
    /// </summary>
    public BodyShapeAnnotationSource Source { get; set; } = BodyShapeAnnotationSource.Manual;

    /// <summary>
    /// Legacy/UI-facing annotation state. Kept in sync with Source via the constructors and helper methods.
    /// </summary>
    [JsonIgnore]
    public BodyShapeAnnotationState AnnotationState { get; set; } = BodyShapeAnnotationState.Manual;

    public BodyShapeDescriptor.LabelSignature ToLabelSignature()
    {
        return new BodyShapeDescriptor.LabelSignature() { Category = Category, Value = Value };
    }

    public override string ToString()
    {
        return this.ToLabelSignature() + " (" + Source.ToString() + ")";
    }

    public static BodyShapeAnnotationSource AnnotationStateToSource(BodyShapeAnnotationState state)
    {
        switch (state)
        {
            case BodyShapeAnnotationState.Manual: return BodyShapeAnnotationSource.Manual;
            case BodyShapeAnnotationState.Library: return BodyShapeAnnotationSource.Library;
            case BodyShapeAnnotationState.RulesBased: return BodyShapeAnnotationSource.RulesBased;
            case BodyShapeAnnotationState.Classifier: return BodyShapeAnnotationSource.Classifier;
            // Mixed/None/legacy mix: fall back to Manual since a single descriptor must have exactly one source.
            default: return BodyShapeAnnotationSource.Manual;
        }
    }

    public static BodyShapeAnnotationState SourceToAnnotationState(BodyShapeAnnotationSource source)
    {
        switch (source)
        {
            case BodyShapeAnnotationSource.Manual: return BodyShapeAnnotationState.Manual;
            case BodyShapeAnnotationSource.Library: return BodyShapeAnnotationState.Library;
            case BodyShapeAnnotationSource.RulesBased: return BodyShapeAnnotationState.RulesBased;
            case BodyShapeAnnotationSource.Classifier: return BodyShapeAnnotationState.Classifier;
            default: return BodyShapeAnnotationState.None;
        }
    }
}

/// <summary>
/// Per-weight-slot preview NPC mapping for the BodySlide preview viewer (Section B).
/// Keys are integer weight slots (0-100); values are male/female NPC FormKeys.
/// int dict keys are safe with Newtonsoft (string-coerced); FormKey dict keys are NOT
/// (Mutagen registers FormKey only as a value-level converter — see RacePreviewNpcsConverter
/// in NifPreviewNpcSettings.cs for why the per-race shape uses a list instead).
/// </summary>
public class OBodyPreviewNpcSettings
{
    public Dictionary<int, PreviewNpcPair> WeightPreviewNpcs { get; set; } = new();
}

[DebuggerDisplay("{SliderName}: {Small} / {Big}")]
public class BodySlideSlider
{
    public string SliderName { get; set; }
    public int Big { get; set; }
    public int Small { get; set; }
}


[DebuggerDisplay("{BodyTypeGroup} Rules: {DescriptorClassifiers.Count}")]
public class SliderClassificationRulesByBodyType
{
    public string BodyTypeGroup { get; set; }
    public List<DescriptorClassificationRuleSet> DescriptorClassifiers { get; set; } = new();
}

[DebuggerDisplay("{DescriptorCategory}")]
public class DescriptorClassificationRuleSet
{
    public string DescriptorCategory { get; set; }
    public string DefaultDescriptorValue { get; set; }
    public List<DescriptorAssignmentRuleSet> RuleList { get; set; } = new();
}

[DebuggerDisplay("{SelectedDescriptorValue}")]
public class DescriptorAssignmentRuleSet
{
    public string SelectedDescriptorValue { get; set; }
    public List<AndGatedSliderRuleGroup> RuleListORlogic { get; set; } = new();
}

public class AndGatedSliderRuleGroup
{
    public List<SliderClassificationRule> RuleListANDlogic { get; set; } = new();
}

[DebuggerDisplay("{SliderName} ({SliderType}) {Comparator} {Value}")]
public class SliderClassificationRule
{
    public string SliderName { get; set; }
    public string Comparator { get; set; }
    public int Value { get; set; }
    public BodySliderType SliderType { get; set; }
}

/// <summary>
/// Which slider value a <see cref="SliderClassificationRule"/> tests. BodySlide presets store two
/// authored values per slider — <see cref="BodySlideSlider.Small"/> (weight 0) and
/// <see cref="BodySlideSlider.Big"/> (weight 100) — and the game blends between them linearly by
/// NPC weight. Serialized by name (StringEnumConverter), so new members are additive-safe.
/// </summary>
public enum BodySliderType
{
    /// <summary>Passes if the comparison holds for the Small OR the Big value. Whole-preset semantics: the result is weight-independent.</summary>
    Either,
    /// <summary>Tests the low-weight (weight 0) authored value. Whole-preset semantics.</summary>
    Small,
    /// <summary>Tests the high-weight (weight 100) authored value. Whole-preset semantics.</summary>
    Big,
    /// <summary>
    /// Tests the value the slider actually has at each of the preset's descriptor weight slots
    /// (<c>Small + (Big - Small) * weight/100</c> — the same linear blend the game applies).
    /// A rule containing an Interpolated condition is evaluated once per weight slot, and the
    /// descriptor lands only in the slots where the rule passes, instead of in every slot.
    /// </summary>
    Interpolated
}

/// <summary>
/// Helpers for working with the per-weight descriptor model on <see cref="BodySlideSetting"/>.
/// Centralizes flatten/iteration/mutation logic so call sites don't have to know the slot layout.
/// </summary>
public static class BodySlideSettingExtensions
{
    /// <summary>
    /// Returns every descriptor across every weight slot. Duplicates are not deduplicated --
    /// callers that need uniqueness should project into a HashSet themselves.
    /// </summary>
    public static IEnumerable<AnnotatedDescriptorSignature> EnumerateAllDescriptors(this BodySlideSetting bs)
    {
        if (bs?.BodyShapeDescriptorsByWeight == null) yield break;
        foreach (var slot in bs.BodyShapeDescriptorsByWeight.Values)
        {
            if (slot == null) continue;
            foreach (var d in slot)
            {
                yield return d;
            }
        }
    }

    /// <summary>
    /// Returns the union of descriptors across all weight slots, deduplicated by Category/Value.
    /// </summary>
    public static HashSet<AnnotatedDescriptorSignature> GetDescriptorUnion(this BodySlideSetting bs)
    {
        var result = new HashSet<AnnotatedDescriptorSignature>();
        foreach (var d in bs.EnumerateAllDescriptors())
        {
            result.Add(d);
        }
        return result;
    }

    /// <summary>
    /// True if any weight slot has at least one descriptor.
    /// </summary>
    public static bool HasAnyDescriptors(this BodySlideSetting bs)
    {
        if (bs?.BodyShapeDescriptorsByWeight == null) return false;
        foreach (var slot in bs.BodyShapeDescriptorsByWeight.Values)
        {
            if (slot != null && slot.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Adds a descriptor to every existing slot. Used for "applies to whole preset" semantics
    /// (rule-based annotations with TargetWeight == null, manual UI in stage 1).
    /// </summary>
    public static void AddDescriptorToAllSlots(this BodySlideSetting bs, AnnotatedDescriptorSignature descriptor)
    {
        if (bs?.BodyShapeDescriptorsByWeight == null || descriptor == null) return;
        foreach (var slot in bs.BodyShapeDescriptorsByWeight.Values)
        {
            slot.Add(new AnnotatedDescriptorSignature(descriptor.ToLabelSignature(), descriptor.Source));
        }
    }

    /// <summary>
    /// Removes descriptors matching <paramref name="match"/> from every weight slot.
    /// </summary>
    public static int RemoveDescriptorsFromAllSlots(this BodySlideSetting bs, Predicate<AnnotatedDescriptorSignature> match)
    {
        if (bs?.BodyShapeDescriptorsByWeight == null) return 0;
        int removed = 0;
        foreach (var slot in bs.BodyShapeDescriptorsByWeight.Values)
        {
            removed += slot.RemoveWhere(match);
        }
        return removed;
    }

    /// <summary>
    /// Clears every slot but preserves the slot keys themselves.
    /// </summary>
    public static void ClearAllDescriptorSlots(this BodySlideSetting bs)
    {
        if (bs?.BodyShapeDescriptorsByWeight == null) return;
        foreach (var slot in bs.BodyShapeDescriptorsByWeight.Values)
        {
            slot.Clear();
        }
    }

    /// <summary>
    /// Replaces the entire per-weight payload with a single set of descriptors duplicated across every existing slot.
    /// Used by stage 1 single-pane UI dump and by VM_BodySlideExchange when a per-weight payload is unavailable.
    /// </summary>
    public static void SetUniformDescriptors(this BodySlideSetting bs, IEnumerable<AnnotatedDescriptorSignature> descriptors)
    {
        if (bs == null) return;
        bs.ClearAllDescriptorSlots();
        if (descriptors == null) return;
        foreach (var d in descriptors)
        {
            bs.AddDescriptorToAllSlots(d);
        }
    }
}