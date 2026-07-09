using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the annotator's catalog-first slider-list helpers on
/// <see cref="VM_SliderClassificationRulesByBodyType"/>: the visible-name computation
/// (catalog by default, preset-contributed names behind the toggle, rule-referenced names always)
/// and the in-place collection reconcile (which must never raise a Reset — a Reset nulls the
/// SelectedItem of every bound rule-row ComboBox).
/// </summary>
public class AnnotatorSliderListTests
{
    private static readonly string[] Catalog = { "Belly", "Breasts", "ShoulderWidth" };
    private static readonly string[] PresetContributed = { "Belly", "Apprentice Bikini Squeeze", "CustomMorph" };

    [Fact]
    public void BuildVisibleSliderNames_CatalogMode_ExcludesPresetOnlyNames()
    {
        var visible = VM_SliderClassificationRulesByBodyType.BuildVisibleSliderNames(
            Catalog, PresetContributed, Enumerable.Empty<string>(), showAll: false);

        visible.Should().Equal("Belly", "Breasts", "ShoulderWidth");
    }

    [Fact]
    public void BuildVisibleSliderNames_ShowAll_IncludesPresetContributedNames()
    {
        var visible = VM_SliderClassificationRulesByBodyType.BuildVisibleSliderNames(
            Catalog, PresetContributed, Enumerable.Empty<string>(), showAll: true);

        visible.Should().Equal("Apprentice Bikini Squeeze", "Belly", "Breasts", "CustomMorph", "ShoulderWidth");
    }

    [Fact]
    public void BuildVisibleSliderNames_RuleReferencedNames_AreAlwaysIncluded()
    {
        // "CustomMorph" is preset-only and the toggle is off, but a saved rule references it —
        // it must stay visible so the rule row's picker doesn't blank out. Null/whitespace
        // references (empty rule rows) are ignored.
        var visible = VM_SliderClassificationRulesByBodyType.BuildVisibleSliderNames(
            Catalog, PresetContributed, new[] { "CustomMorph", null, " " }, showAll: false);

        visible.Should().Equal("Belly", "Breasts", "CustomMorph", "ShoulderWidth");
    }

    [Fact]
    public void BuildVisibleSliderNames_DeduplicatesCaseInsensitively()
    {
        var visible = VM_SliderClassificationRulesByBodyType.BuildVisibleSliderNames(
            new[] { "Belly" }, new[] { "BELLY" }, new[] { "belly" }, showAll: true);

        visible.Should().ContainSingle().Which.Should().Be("Belly");
    }

    [Fact]
    public void ReconcileSliderNameCollection_ProducesDesiredOrder_WithoutReset()
    {
        var target = new ObservableCollection<string> { "Belly", "Zap1", "Breasts", "Appended" };
        var resetRaised = false;
        ((INotifyCollectionChanged)target).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resetRaised = true;
        };

        var desired = new[] { "Appended", "Belly", "Breasts", "NewName" };
        VM_SliderClassificationRulesByBodyType.ReconcileSliderNameCollection(target, desired);

        target.Should().Equal("Appended", "Belly", "Breasts", "NewName");
        resetRaised.Should().BeFalse("a Reset event would null the SelectedItem of bound ComboBoxes");
    }

    [Fact]
    public void ReconcileSliderNameCollection_RetainedItemsSurviveShrinking()
    {
        // Toggle-off case: many preset-only names vanish, the catalog + referenced names stay.
        var target = new ObservableCollection<string> { "Apprentice Bikini Squeeze", "Belly", "Breasts", "CustomMorph", "ShoulderWidth" };

        var desired = new[] { "Belly", "Breasts", "CustomMorph", "ShoulderWidth" };
        VM_SliderClassificationRulesByBodyType.ReconcileSliderNameCollection(target, desired);

        target.Should().Equal(desired);
    }

    [Fact]
    public void ReconcileSliderNameCollection_MatchesExistingItemsCaseInsensitively()
    {
        // The collection may hold a different casing (e.g. appended from a hand-edited rule);
        // reconcile must treat it as the same item rather than duplicating it.
        var target = new ObservableCollection<string> { "belly", "Breasts" };

        var desired = new[] { "Belly", "Breasts" };
        VM_SliderClassificationRulesByBodyType.ReconcileSliderNameCollection(target, desired);

        target.Should().HaveCount(2);
        target[0].Should().BeOneOf("belly", "Belly");
        target[1].Should().Be("Breasts");
    }
}
