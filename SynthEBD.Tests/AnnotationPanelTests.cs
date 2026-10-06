using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for the annotation Panel's pure logic: <see cref="AnnotationPanelLayout"/> (sorting, the
/// Evenly spaced quantile pick, paging) and <see cref="AnnotationVerdictWriter"/> (the model write
/// behind <see cref="VM_AnnotationQueue.WriteVerdict"/>).
/// </summary>
public class AnnotationPanelTests
{
    private static PanelSample S(string label, double? value, int weight = 50) => new(label, weight, value);

    private static List<string> Labels(IReadOnlyList<PanelSample> samples, IReadOnlyList<int> order)
        => order.Select(i => samples[i].PresetLabel).ToList();

    // ---------- sorting ----------

    [Fact]
    public void Sort_LowToHigh_MissingLast_TiesByLabelThenWeight()
    {
        var samples = new[] { S("C", 2), S("None", null), S("B", 1, 100), S("B", 1, 0), S("A", 1), S("Nan", double.NaN) };
        var order = AnnotationPanelLayout.SortByValue(samples);
        order.Select(i => (samples[i].PresetLabel, samples[i].Weight)).Should().Equal(
            ("A", 50), ("B", 0), ("B", 100), ("C", 50), ("Nan", 50), ("None", 50));
    }

    // ---------- evenly spaced ----------

    [Fact]
    public void EvenlySpaced_PicksEvenQuantiles()
    {
        // 10 samples valued 0..9; N = 5 -> ranks floor((i + 0.5) * 2) = 1, 3, 5, 7, 9.
        var samples = Enumerable.Range(0, 10).Select(i => S("P" + i, i)).ToList();
        Labels(samples, AnnotationPanelLayout.PickEvenlySpaced(samples, 5))
            .Should().Equal("P1", "P3", "P5", "P7", "P9");
    }

    [Fact]
    public void EvenlySpaced_NAtLeastPopulation_ReturnsAllValued_Sorted()
    {
        var samples = new[] { S("B", 2), S("A", 1), S("X", null) };
        Labels(samples, AnnotationPanelLayout.PickEvenlySpaced(samples, 24)).Should().Equal("A", "B");
    }

    [Fact]
    public void EvenlySpaced_ExcludesMissingAndNaN()
    {
        var samples = new[] { S("A", 1), S("N", double.NaN), S("M", null), S("B", 2), S("C", 3) };
        var picks = AnnotationPanelLayout.PickEvenlySpaced(samples, 2);
        Labels(samples, picks).Should().Equal("A", "C"); // ranks floor(0.25*3)=0, floor(0.75*3)=2
    }

    [Fact]
    public void EvenlySpaced_NoRepeats_AndDeterministicUnderReordering()
    {
        var samples = Enumerable.Range(0, 37).Select(i => S("P" + (i % 7), i / 3, i)).ToList();
        var a = Labels(samples, AnnotationPanelLayout.PickEvenlySpaced(samples, 11));
        var reversed = samples.AsEnumerable().Reverse().ToList();
        var pickedReversed = AnnotationPanelLayout.PickEvenlySpaced(reversed, 11);

        AnnotationPanelLayout.PickEvenlySpaced(samples, 11).Should().OnlyHaveUniqueItems();
        pickedReversed.Select(i => (reversed[i].PresetLabel, reversed[i].Weight))
            .Should().Equal(AnnotationPanelLayout.PickEvenlySpaced(samples, 11).Select(i => (samples[i].PresetLabel, samples[i].Weight)));
        a.Should().HaveCount(11);
    }

    [Fact]
    public void EvenlySpaced_Ties_ResolveByLabelThenWeight()
    {
        // All tied: the ladder walks the (label, weight) order.
        var samples = new[] { S("D", 5), S("B", 5), S("C", 5), S("A", 5) };
        Labels(samples, AnnotationPanelLayout.PickEvenlySpaced(samples, 2)).Should().Equal("B", "D"); // ranks 1, 3
    }

    [Fact]
    public void EvenlySpaced_ZeroOrEmpty_PicksNothing()
    {
        AnnotationPanelLayout.PickEvenlySpaced(new[] { S("A", 1) }, 0).Should().BeEmpty();
        AnnotationPanelLayout.PickEvenlySpaced(new PanelSample[0], 5).Should().BeEmpty();
    }

    // ---------- paging ----------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(22, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(48, 2)]
    [InlineData(49, 3)]
    public void PageCount_24PerPage(int total, int pages)
    {
        AnnotationPanelLayout.PageCount(total, 24).Should().Be(pages);
    }

    [Fact]
    public void PageCount_DefaultIs100PerPage()
    {
        AnnotationPanelLayout.PageCount(100).Should().Be(1);
        AnnotationPanelLayout.PageCount(101).Should().Be(2);
    }

    [Fact]
    public void PageRange_LastPageIsPartial_AndIndexIsClamped()
    {
        AnnotationPanelLayout.PageRange(50, 0, 24).Should().Be((0, 24));
        AnnotationPanelLayout.PageRange(50, 2, 24).Should().Be((48, 2));
        AnnotationPanelLayout.PageRange(50, 9, 24).Should().Be((48, 2));
        AnnotationPanelLayout.PageRange(22, 0, 24).Should().Be((0, 22)); // a 22-case worklist: one page of 22
        AnnotationPanelLayout.PageRange(0, 0, 24).Should().Be((0, 0));
    }

    [Fact]
    public void Paging_NonPositivePageSize_IsTreatedAsOne()
    {
        AnnotationPanelLayout.PageCount(3, 0).Should().Be(3);
        AnnotationPanelLayout.PageRange(3, 1, 0).Should().Be((1, 1));
    }

    // ---------- free order ----------

    [Theory]
    [InlineData(0, 3, 5, 2)]   // drag forward: insertion point after removal shifts back one
    [InlineData(4, 1, 5, 1)]   // drag backward: lands at the insertion point
    [InlineData(2, 2, 5, 2)]   // dropped on itself
    [InlineData(2, 3, 5, 2)]   // dropped just after itself
    [InlineData(1, 5, 5, 4)]   // dropped at the end
    public void MoveTarget_AccountsForRemovalShift(int from, int insertIndex, int count, int expected)
    {
        AnnotationPanelLayout.MoveTarget(from, insertIndex, count).Should().Be(expected);
    }

    [Fact]
    public void FormatOrder_RoundTripsAsWorklist()
    {
        var text = AnnotationPanelLayout.FormatOrder("Panel order: Butt", new (string, int, IReadOnlyList<string>)[]
        {
            ("Preset B", 100, new string[0]),
            ("Preset A", 0, new[] { "Preset A Copy" }),
        });
        text.Should().StartWith("# Panel order: Butt");

        var cases = AnnotationCaseList.Parse(text, out var warnings);
        warnings.Should().BeEmpty();
        cases.Select(c => (c.PresetLabel, c.Weight)).Should().Equal(("Preset B", (int?)100), ("Preset A", (int?)0));
        cases[1].Note.Should().Be("aliases: Preset A Copy");
    }

    // ---------- verdict writer ----------

    private static PresetAnnotation Ann(string label, int weight, params (string Cat, string Val)[] descriptors) => new()
    {
        PresetLabel = label,
        PresetGender = Gender.Female,
        Weight = weight,
        Descriptors = descriptors.Select(d => new BodyShapeDescriptor.LabelSignature { Category = d.Cat, Value = d.Val }).ToList(),
    };

    private static List<(string, Gender, int)> Family(params string[] labels)
        => labels.Select(l => (l, Gender.Female, 50)).ToList();

    private static List<(string, string)> Pairs(PresetAnnotation a)
        => a.Descriptors.Select(d => (d.Category, d.Value)).ToList();

    [Fact]
    public void Write_RepresentativeAndAliases_CreateAndStampAliases()
    {
        var store = new List<PresetAnnotation>();
        var written = AnnotationVerdictWriter.Write(store, Family("Rep", "Alias1", "Alias2"), "Butt", new[] { "Round" });

        store.Should().HaveCount(3);
        written.Should().OnlyContain(a => a != null);
        foreach (var a in store) Pairs(a).Should().Equal(("Butt", "Round"));
        store.Single(a => a.PresetLabel == "Rep").AliasLabels.Should().Equal("Alias1", "Alias2");
        store.Single(a => a.PresetLabel == "Alias1").AliasLabels.Should().Equal("Rep", "Alias2");
    }

    [Fact]
    public void Write_ReplacesOnlyTargetCategory()
    {
        var store = new List<PresetAnnotation> { Ann("Rep", 50, ("Belly", "Fat"), ("Butt", "Flat")) };
        AnnotationVerdictWriter.Write(store, Family("Rep"), "Butt", new[] { "Large", "Round" });

        Pairs(store.Single()).Should().Equal(("Belly", "Fat"), ("Butt", "Large"), ("Butt", "Round"));
    }

    [Fact]
    public void Write_EmptySet_RemovesCategory_AndDropsEmptyAnnotation()
    {
        var store = new List<PresetAnnotation>
        {
            Ann("Rep", 50, ("Butt", "Flat")),
            Ann("Alias", 50, ("Belly", "Fat"), ("Butt", "Flat")),
        };
        var written = AnnotationVerdictWriter.Write(store, Family("Rep", "Alias", "Unannotated"), "Butt", new string[0]);

        store.Should().ContainSingle().Which.PresetLabel.Should().Be("Alias");
        Pairs(store.Single()).Should().Equal(("Belly", "Fat"));
        written[0].Should().BeNull();
        written[1].Should().NotBeNull();
        written[2].Should().BeNull(); // no annotation is created just to hold nothing
    }

    [Fact]
    public void Write_OtherWeightsAndGenders_Untouched()
    {
        var other = Ann("Rep", 0, ("Butt", "Flat"));
        var store = new List<PresetAnnotation> { other };
        AnnotationVerdictWriter.Write(store, Family("Rep"), "Butt", new[] { "Round" });

        Pairs(other).Should().Equal(("Butt", "Flat"));
        store.Should().HaveCount(2);
    }

    [Fact]
    public void ReadValues_ReturnsOnlyCategoryValues()
    {
        var store = new List<PresetAnnotation> { Ann("Rep", 50, ("Belly", "Fat"), ("Butt", "Round"), ("Butt", "Large")) };
        AnnotationVerdictWriter.ReadValues(store, "Rep", Gender.Female, 50, "Butt").Should().Equal("Round", "Large");
        AnnotationVerdictWriter.ReadValues(store, "Rep", Gender.Male, 50, "Butt").Should().BeEmpty();
    }
}
