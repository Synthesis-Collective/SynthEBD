using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the shareable Body Type Rules files (<see cref="BodyTypeRuleSet"/>), their load / active-file
/// projection / fold-back / save cycle (<see cref="SettingsIO_BodyTypeRules"/>), the worklist-annotation
/// apply step (<see cref="PresetAnnotationApplier"/>), and the per-(slot, category) manual precedence in
/// the measurement classifier merge.
/// </summary>
public class BodyTypeRuleFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SynthEBD_BodyTypeRules_" + Guid.NewGuid().ToString("N"));

    public BodyTypeRuleFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---------- fixtures ----------

    private static SliderClassificationRulesByBodyType SliderRules(string bodyType, string category, string? defaultValue, params string[] ruleValues) => new()
    {
        BodyTypeGroup = bodyType,
        DescriptorClassifiers = new()
        {
            new DescriptorClassificationRuleSet
            {
                DescriptorCategory = category,
                DefaultDescriptorValue = defaultValue,
                RuleList = ruleValues.Select(v => new DescriptorAssignmentRuleSet
                {
                    SelectedDescriptorValue = v,
                    RuleListORlogic = new()
                    {
                        new AndGatedSliderRuleGroup
                        {
                            RuleListANDlogic = new() { new SliderClassificationRule { SliderName = "MuscleAbs", SliderType = BodySliderType.Interpolated, Comparator = ">=", Value = 60 } },
                        },
                    },
                }).ToList(),
            },
        },
    };

    private static BodyTypeProfile Profile(string bodyType) => new()
    {
        Name = bodyType + " profile",
        BodyTypeName = bodyType,
        Rules = new()
        {
            new MeasurementRule
            {
                Descriptor = new BodyShapeDescriptor.LabelSignature { Category = "Arms", Value = "Thick" },
                GroupsORlogic = new()
                {
                    new AndGatedMeasurementGroup
                    {
                        ConditionsANDlogic = new()
                        {
                            new MeasurementCondition { Kind = MeasurementConditionKind.DescriptorRef, RefCategory = "Build", RefValue = "Powerful" },
                        },
                    },
                },
            },
        },
        PresetAnnotations = new() { new PresetAnnotation { PresetLabel = "P", Weight = 50 } },
    };

    private static List<BodyShapeDescriptorShell> Catalog(params (string Category, string[] Values)[] shells) =>
        shells.Select(s => new BodyShapeDescriptorShell
        {
            Category = s.Category,
            CategoryDescription = s.Category + " description",
            Descriptors = s.Values.Select(v => new BodyShapeDescriptor
            {
                ID = new BodyShapeDescriptor.LabelSignature { Category = s.Category, Value = v },
                ValueDescription = v + " description",
                AssociatedRules = new BodyShapeDescriptorRules { AllowedRaceGroupings = new() { "Humanoid" } },
            }).ToList(),
        }).ToList();

    private string WriteFile(string fileName, BodyTypeRuleSet ruleSet)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, JSONhandler<BodyTypeRuleSet>.Serialize(ruleSet, out _, out _));
        return path;
    }

    // ---------- BodyTypeRuleSet ----------

    [Fact]
    public void PersonalState_IsNotWrittenToTheRuleFile()
    {
        var ruleSet = new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", MeasurementProfile = Profile("CBBE 3BA") };

        var json = JSONhandler<BodyTypeRuleSet>.Serialize(ruleSet, out bool ok, out _);

        ok.Should().BeTrue();
        json.Should().NotContain("PresetAnnotations").And.NotContain("AnnotatorPrefs");
        var back = JSONhandler<BodyTypeRuleSet>.Deserialize(json, out ok, out _);
        ok.Should().BeTrue();
        back.MeasurementProfile!.Rules.Should().ContainSingle();
    }

    [Fact]
    public void DescriptorDefinitions_CarryOnlyReferencedDescriptors_WithoutDistributionRules()
    {
        var ruleSet = new BodyTypeRuleSet
        {
            BodyTypeName = "CBBE 3BA",
            SliderRules = SliderRules("CBBE 3BA", "Belly", "Normal", "Muscular"),
            MeasurementProfile = Profile("CBBE 3BA"),
        };
        var catalog = Catalog(("Belly", new[] { "Normal", "Muscular", "Chubby" }),
                              ("Arms", new[] { "Thick", "Small" }),
                              ("Build", new[] { "Powerful", "Slight" }),
                              ("Chest", new[] { "Busty" }));

        ruleSet.FoldLegacySliderRules(null);
        ruleSet.RefreshDescriptorDefinitions(catalog);

        var carried = ruleSet.DescriptorDefinitions.SelectMany(s => s.Descriptors.Select(d => s.Category + ":" + d.ID.Value)).ToList();
        carried.Should().BeEquivalentTo("Belly:Normal", "Belly:Muscular", "Arms:Thick", "Build:Powerful");
        ruleSet.DescriptorDefinitions.SelectMany(s => s.Descriptors)
            .Should().OnlyContain(d => d.AssociatedRules.AllowedRaceGroupings.Count == 0 && d.ValueDescription.EndsWith("description"));
    }

    [Fact]
    public void FindMissing_OffersOnlyAbsentValues_AndHonorsDeclines()
    {
        var ruleSet = new BodyTypeRuleSet
        {
            BodyTypeName = "CBBE 3BA",
            FilePath = Path.Combine(_dir, "shared.json"),
            DescriptorDefinitions = Catalog(("Arms", new[] { "Thick", "Small" }), ("Build", new[] { "Powerful" }), ("Waist", new[] { "Narrow" })),
        };
        var userCatalog = Catalog(("Arms", new[] { "Small" }));
        var declined = new HashSet<string> { BodyTypeRuleSet.DeclineKey("shared.json", "Waist", "Narrow") };

        var missing = ruleSet.FindMissingDescriptorDefinitions(userCatalog, declined);

        missing.Select(s => s.Category).Should().BeEquivalentTo("Arms", "Build");
        var arms = missing.Single(s => s.Category == "Arms");
        arms.Descriptors.Select(d => d.ID.Value).Should().Equal("Thick");
        arms.CategoryDescription.Should().BeEmpty("an existing category's description is the user's and is never offered");
        missing.Single(s => s.Category == "Build").CategoryDescription.Should().Be("Build description");
    }

    [Fact]
    public void MergeMissing_NeverOverwritesExistingDefinitions()
    {
        var userCatalog = Catalog(("Arms", new[] { "Small" }));
        userCatalog[0].CategoryDescription = "mine";
        userCatalog[0].Descriptors[0].ValueDescription = "my small";
        var incoming = Catalog(("Arms", new[] { "Small", "Thick" }), ("Build", new[] { "Powerful" }));

        int added = BodyTypeRuleSet.MergeMissingDescriptorDefinitions(userCatalog, incoming);

        added.Should().Be(2);
        userCatalog.Single(s => s.Category == "Arms").CategoryDescription.Should().Be("mine");
        userCatalog.Single(s => s.Category == "Arms").Descriptors.Single(d => d.ID.Value == "Small").ValueDescription.Should().Be("my small");
        userCatalog.Single(s => s.Category == "Arms").Descriptors.Select(d => d.ID.Value).Should().BeEquivalentTo("Small", "Thick");
        userCatalog.Should().Contain(s => s.Category == "Build");
    }

    // ---------- SettingsIO_BodyTypeRules ----------

    [Fact]
    public void ActiveFile_FollowsSelection_ElseFirstByDisplayName()
    {
        WriteFile("b.json", new BodyTypeRuleSet { Name = "Beta", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "Belly", "Normal") });
        WriteFile("a.json", new BodyTypeRuleSet { Name = "Zulu", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "Belly", "Chubby") });
        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);
        var settings = new Settings_OBody();

        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);
        settings.BodyTypeProfiles.Single().DefaultDescriptorValuesByCategory["Belly"].Should().Be("Normal", "Beta sorts before Zulu");
        SettingsIO_BodyTypeRules.GetSelectedFileName(settings, "cbbe 3ba").Should().Be("b.json");

        SettingsIO_BodyTypeRules.SetSelectedFileName(settings, "CBBE 3BA", "a.json");
        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);
        settings.BodyTypeProfiles.Single().DefaultDescriptorValuesByCategory["Belly"].Should().Be("Chubby");
    }

    [Fact]
    public void Views_AreDeepCopies()
    {
        WriteFile("x.json", new BodyTypeRuleSet { Name = "X", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "Belly", "Normal"), MeasurementProfile = Profile("CBBE 3BA") });
        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);
        var settings = new Settings_OBody();

        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);
        settings.BodyTypeProfiles[0].Rules.Clear();

        ruleSets[0].MeasurementProfile!.Rules.Should().NotBeEmpty();
    }

    [Fact]
    public void Save_CreatesFilesOnlyForAuthoredContent_AndSkipsUnchangedFiles()
    {
        var existingPath = WriteFile("3ba.json", new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "Belly", "Normal") });
        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);
        var settings = new Settings_OBody();
        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);

        // First save normalizes the hand-written file once (descriptor definitions etc.).
        SettingsIO_BodyTypeRules.SaveRuleSets(settings, ruleSets, _dir, null, out var errors).Should().BeTrue(errors);
        var stamp = File.GetLastWriteTimeUtc(existingPath);

        // BHUNP: a new profile -> gets a file. HIMBO has no profile -> no file.
        settings.BodyTypeProfiles.Add(Profile("BHUNP"));
        System.Threading.Thread.Sleep(20);

        SettingsIO_BodyTypeRules.SaveRuleSets(settings, ruleSets, _dir, null, out errors).Should().BeTrue(errors);

        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().BeEquivalentTo("3ba.json", "BHUNP.json");
        File.GetLastWriteTimeUtc(existingPath).Should().Be(stamp, "an unchanged file is not rewritten");
        SettingsIO_BodyTypeRules.GetSelectedFileName(settings, "BHUNP").Should().Be("BHUNP.json");
    }

    [Fact]
    public void Save_RemovedProfileIsDroppedFromTheActiveFile()
    {
        WriteFile("3ba.json", new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "Belly", "Normal"), MeasurementProfile = Profile("CBBE 3BA") });
        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);
        var settings = new Settings_OBody();
        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);

        settings.BodyTypeProfiles.Clear();
        SettingsIO_BodyTypeRules.SaveRuleSets(settings, ruleSets, _dir, null, out _);

        var reloaded = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null).Single();
        reloaded.MeasurementProfile.Should().BeNull();
        reloaded.SliderRules.Should().BeNull("converted slider rules are never written back");
    }

    // ---------- Label by Sliders migration ----------

    [Fact]
    public void Load_ConvertsLegacySliderRulesIntoTheProfile_AndSaveDropsThem()
    {
        var path = WriteFile("3ba.json", new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", SliderRules = SliderRules("CBBE 3BA", "BellyMorph", "Low Definition", "Muscular"), MeasurementProfile = Profile("CBBE 3BA") });

        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);
        var ruleSet = ruleSets.Single();
        ruleSet.SliderRules.Should().BeNull();
        var converted = ruleSet.MeasurementProfile!.Rules.Single(r => r.Descriptor.Category == "BellyMorph");
        converted.Descriptor.Value.Should().Be("Muscular");
        converted.GroupsORlogic.Single().ConditionsANDlogic.Single().Kind.Should().Be(MeasurementConditionKind.Slider);
        ruleSet.MeasurementProfile.DefaultDescriptorValuesByCategory["BellyMorph"].Should().Be("Low Definition");
        ruleSet.MeasurementProfile.Name.Should().Be("CBBE 3BA profile", "the existing profile keeps its name, so its measurement cache file stays the same");

        var settings = new Settings_OBody();
        SettingsIO_BodyTypeRules.ApplyActiveRuleSetViews(settings, ruleSets);
        SettingsIO_BodyTypeRules.SaveRuleSets(settings, ruleSets, _dir, null, out var errors).Should().BeTrue(errors);

        File.ReadAllText(path).Should().NotContain("SliderRules").And.Contain("\"Slider\"");
        var reloaded = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null).Single();
        reloaded.MeasurementProfile!.Rules.Count(r => r.Descriptor.Category == "BellyMorph").Should().Be(1, "re-loading converts nothing twice");
    }

    [Fact]
    public void LegacyOBodySettingsSliderRules_AreFoldedIntoRuleFiles()
    {
        WriteFile("3ba.json", new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", MeasurementProfile = Profile("CBBE 3BA") });
        var json = "{ \"BodySlideClassificationRules\": { \"CBBE 3BA\": " + JSONhandler<SliderClassificationRulesByBodyType>.Serialize(SliderRules("CBBE 3BA", "Belly", null, "Muscular"), out _, out _)
                 + ", \"HIMBO\": " + JSONhandler<SliderClassificationRulesByBodyType>.Serialize(SliderRules("HIMBO", "Build", "Medium", "Powerful"), out _, out _) + " } }";
        var settings = JSONhandler<Settings_OBody>.Deserialize(json, out bool ok, out var error);
        ok.Should().BeTrue(error);
        var ruleSets = SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null);

        SettingsIO_BodyTypeRules.FoldLegacyOBodySliderRules(settings, ruleSets, _dir, null).Should().Be(2);

        settings.LegacyBodySlideClassificationRules.Should().BeNull();
        ruleSets.Single(r => r.BodyTypeName == "CBBE 3BA").MeasurementProfile!.Rules.Should().Contain(r => r.Descriptor.Category == "Belly" && r.Descriptor.Value == "Muscular");
        var himbo = ruleSets.Single(r => r.BodyTypeName == "HIMBO");
        himbo.MeasurementProfile!.Rules.Should().ContainSingle(r => r.Descriptor.Value == "Powerful");
        himbo.MeasurementProfile.DefaultDescriptorValuesByCategory["Build"].Should().Be("Medium");
        SettingsIO_BodyTypeRules.GetSelectedFileName(settings, "HIMBO").Should().Be("HIMBO.json");

        JSONhandler<Settings_OBody>.Serialize(settings, out _, out _).Should().NotContain("BodySlideClassificationRules");
    }

    [Fact]
    public void Duplicate_GetsUniqueNameAndPath()
    {
        var source = new BodyTypeRuleSet { Name = "3BA", BodyTypeName = "CBBE 3BA", FilePath = Path.Combine(_dir, "3BA.json"), MeasurementProfile = Profile("CBBE 3BA") };
        var ruleSets = new List<BodyTypeRuleSet> { source };

        var first = SettingsIO_BodyTypeRules.Duplicate(source, _dir, ruleSets);
        ruleSets.Add(first);
        var second = SettingsIO_BodyTypeRules.Duplicate(source, _dir, ruleSets);

        first.Name.Should().Be("3BA (copy)");
        second.Name.Should().Be("3BA (copy) 2");
        first.FilePath.Should().NotBe(second.FilePath);
        first.MeasurementProfile!.Id.Should().Be(source.MeasurementProfile!.Id, "the copy shares the measurement cache");
        first.MeasurementProfile.Should().NotBeSameAs(source.MeasurementProfile);
    }

    [Fact]
    public void LoadRuleSets_SkipsUnreadableFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "nobodytype.json"), "{ \"Name\": \"x\" }");
        WriteFile("ok.json", new BodyTypeRuleSet { Name = "ok", BodyTypeName = "UBE" });

        SettingsIO_BodyTypeRules.LoadRuleSets(_dir, null).Select(r => r.FileName).Should().Equal("ok.json");
    }

    // ---------- PresetAnnotationApplier ----------

    private static BodySlideSetting Preset(string label) => new() { Label = label };

    private static PresetAnnotation Annotation(string label, int weight, params (string Category, string Value)[] descriptors) => new()
    {
        PresetLabel = label,
        PresetGender = Gender.Female,
        Weight = weight,
        Descriptors = descriptors.Select(d => new BodyShapeDescriptor.LabelSignature { Category = d.Category, Value = d.Value }).ToList(),
    };

    private static void AddToSlot(BodySlideSetting preset, int weight, string category, string value, BodyShapeAnnotationSource source) =>
        preset.BodyShapeDescriptorsByWeight[weight].Add(new AnnotatedDescriptorSignature(new BodyShapeDescriptor.LabelSignature { Category = category, Value = value }, source));

    [Fact]
    public void Apply_ReplacesTheCategoryInTheSlot_AsManual()
    {
        var preset = Preset("P");
        AddToSlot(preset, 50, "Arms", "Athletic", BodyShapeAnnotationSource.RulesBased);
        AddToSlot(preset, 50, "Belly", "Normal", BodyShapeAnnotationSource.RulesBased);
        var known = new HashSet<(string, string)> { ("Arms", "Thick"), ("Arms", "Athletic") };

        var plan = PresetAnnotationApplier.Plan(new[] { Annotation("P", 50, ("Arms", "Thick")) }, (l, g) => l == "P" ? preset : null, known);
        plan.Conflicts.Should().BeEmpty("the slot had no manual Arms value");
        PresetAnnotationApplier.Execute(plan);

        var slot = preset.BodyShapeDescriptorsByWeight[50];
        slot.Where(d => d.Category == "Arms").Should().ContainSingle()
            .Which.Should().Match<AnnotatedDescriptorSignature>(d => d.Value == "Thick" && d.Source == BodyShapeAnnotationSource.Manual);
        slot.Should().Contain(d => d.Category == "Belly", "other categories are untouched");
        preset.BodyShapeDescriptorsByWeight[0].Should().BeEmpty("other weight slots are untouched");
    }

    [Fact]
    public void Apply_ReportsConflicts_AndIsANoOpWhenAlreadyApplied()
    {
        var preset = Preset("P");
        AddToSlot(preset, 50, "Arms", "Small", BodyShapeAnnotationSource.Manual);
        AddToSlot(preset, 75, "Arms", "Thick", BodyShapeAnnotationSource.Manual);
        var annotations = new[] { Annotation("P", 50, ("Arms", "Thick")), Annotation("P", 75, ("Arms", "Thick")) };

        var plan = PresetAnnotationApplier.Plan(annotations, (l, g) => preset, null);

        plan.Changes.Should().ContainSingle().Which.Weight.Should().Be(50);
        plan.Conflicts.Should().ContainSingle().Which.Should().Contain("Small -> Thick");
    }

    [Fact]
    public void Apply_SkipsMissingPresetsSlotsAndUnknownDescriptors()
    {
        var preset = Preset("P");
        var annotations = new[]
        {
            Annotation("Gone", 50, ("Arms", "Thick")),
            Annotation("P", 60, ("Arms", "Thick")),
            Annotation("P", 50, ("Arms", "Imaginary")),
        };

        var plan = PresetAnnotationApplier.Plan(annotations, (l, g) => l == "P" ? preset : null, new HashSet<(string, string)> { ("Arms", "Thick") });

        plan.Changes.Should().BeEmpty();
        plan.MissingPresets.Should().ContainSingle();
        plan.MissingSlots.Should().ContainSingle();
        plan.UnknownDescriptors.Should().Equal("Arms: Imaginary");
    }

    // ---------- measurement merge: manual wins per (slot, category) ----------

    [Fact]
    public void MeasurementMerge_ManualCategoryBlocksEveryClassifierValueInThatCategory()
    {
        var slot = new HashSet<AnnotatedDescriptorSignature>
        {
            new(new BodyShapeDescriptor.LabelSignature { Category = "Arms", Value = "Thick" }, BodyShapeAnnotationSource.Manual),
        };
        var results = new[]
        {
            new AnnotatedDescriptorSignature(new BodyShapeDescriptor.LabelSignature { Category = "Arms", Value = "Athletic" }, BodyShapeAnnotationSource.Classifier),
            new AnnotatedDescriptorSignature(new BodyShapeDescriptor.LabelSignature { Category = "Belly", Value = "Normal" }, BodyShapeAnnotationSource.Classifier),
        };

        BodySlideMeasurementEvaluator.MergeIntoSlot(slot, results).Should().Be(1);

        slot.Select(d => d.Category + ":" + d.Value).Should().BeEquivalentTo("Arms:Thick", "Belly:Normal");
    }
}
