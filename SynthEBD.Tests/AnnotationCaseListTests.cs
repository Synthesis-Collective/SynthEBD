using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Coverage for <see cref="AnnotationCaseList"/>, the worklist parser behind the annotation
/// queue's <see cref="AnnotationQueuePolicy.List"/> mode.
/// <para>The formats exist to be forgiving: a list is usually pasted out of a chat or a script, and
/// a reviewer should not have to learn a syntax before they can start judging. So the assertions
/// here are mostly about what the parser accepts without being told, and about the two properties
/// it must never violate -- the listed order is preserved exactly, and a bad line costs that line
/// rather than the list.</para>
/// </summary>
public class AnnotationCaseListTests
{
    // ---------- plain text ----------

    [Fact]
    public void PlainText_PipeSeparatedCarriesWeightAndNote()
    {
        var cases = AnnotationCaseList.Parse(
            "3BA Willendorf | 75 | Spine_to_BellyFlab 19.36 but sternum_to_belly 0.45",
            out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle();
        cases[0].PresetLabel.Should().Be("3BA Willendorf");
        cases[0].Weight.Should().Be(75);
        cases[0].Note.Should().Be("Spine_to_BellyFlab 19.36 but sternum_to_belly 0.45");
    }

    [Fact]
    public void PlainText_AcceptsTabAndTrailingNumberWithoutASeparator()
    {
        var cases = AnnotationCaseList.Parse(
            "Nami Extra Curvy\t100\nCustomPresetChubby2 25\nCP Anime, 50",
            out var warnings);

        warnings.Should().BeEmpty();
        cases.Select(c => (c.PresetLabel, c.Weight)).Should().Equal(
            ("Nami Extra Curvy", 100),
            ("CustomPresetChubby2", 25),
            ("CP Anime", 50));
    }

    [Fact]
    public void PlainText_BarePresetNameMeansEveryWeight()
    {
        var cases = AnnotationCaseList.Parse("3BA Veias", out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle();
        cases[0].PresetLabel.Should().Be("3BA Veias");
        cases[0].Weight.Should().BeNull("a case with no weight asks for every weight the preset has");
    }

    [Fact]
    public void PlainText_SkipsBlankLinesAndComments()
    {
        var cases = AnnotationCaseList.Parse(
            "# Belly boundary cases, generated 2026-09-18\n"
            + "\n"
            + "// second comment style\n"
            + "3BA Willendorf | 75\n",
            out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle().Which.PresetLabel.Should().Be("3BA Willendorf");
    }

    [Fact]
    public void PlainText_PreservesTheListedOrderExactly()
    {
        // The order is the author's deliberate choice; List mode exists precisely so it survives.
        var cases = AnnotationCaseList.Parse("C | 0\nA | 50\nB | 100\nA | 0", out _);

        cases.Select(c => c.PresetLabel + "@" + c.Weight).Should().Equal("C@0", "A@50", "B@100", "A@0");
    }

    [Fact]
    public void PlainText_AcceptsAWeightWrittenTheWayTheRowHeaderShowsIt()
    {
        var cases = AnnotationCaseList.Parse("3BA Willendorf | w75", out var warnings);

        warnings.Should().BeEmpty();
        cases[0].Weight.Should().Be(75, "row headers read \"(W75, Female)\", so w75 is what a user copies");
    }

    [Fact]
    public void PlainText_ANameEndingInALongNumberIsNotMistakenForAWeight()
    {
        // The trailing-weight rule is bounded to three digits so a versioned or dated preset name
        // is not silently truncated.
        var cases = AnnotationCaseList.Parse("Some Preset 2026", out _);

        cases[0].PresetLabel.Should().Be("Some Preset 2026");
        cases[0].Weight.Should().BeNull();
    }

    [Fact]
    public void PlainText_OutOfRangeWeightWarnsAndFallsBackToEveryWeight()
    {
        var cases = AnnotationCaseList.Parse("3BA Willendorf | 150", out var warnings);

        cases.Should().ContainSingle();
        cases[0].Weight.Should().BeNull();
        warnings.Should().ContainSingle().Which.Should().Contain("150");
    }

    [Fact]
    public void PlainText_OneBadLineCostsThatLineNotTheList()
    {
        var cases = AnnotationCaseList.Parse("  |  | orphan note\n3BA Willendorf | 75", out var warnings);

        cases.Should().ContainSingle().Which.PresetLabel.Should().Be("3BA Willendorf");
        warnings.Should().ContainSingle().Which.Should().Contain("no preset name");
    }

    // ---------- JSON ----------

    [Fact]
    public void Json_BareArrayOfObjects()
    {
        var cases = AnnotationCaseList.Parse(
            "[ { \"preset\": \"3BA Willendorf\", \"weight\": 75, \"note\": \"boundary\" } ]",
            out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle();
        cases[0].PresetLabel.Should().Be("3BA Willendorf");
        cases[0].Weight.Should().Be(75);
        cases[0].Note.Should().Be("boundary");
    }

    [Fact]
    public void Json_ObjectWithRowsArray()
    {
        var cases = AnnotationCaseList.Parse(
            "{ \"category\": \"Belly\", \"rows\": [ { \"preset\": \"A\", \"weight\": 0 }, { \"preset\": \"B\" } ] }",
            out var warnings);

        warnings.Should().BeEmpty();
        cases.Select(c => c.PresetLabel).Should().Equal("A", "B");
        cases[1].Weight.Should().BeNull();
    }

    [Fact]
    public void Json_AVerdictExportRoundTripsBackInAsAWorklist()
    {
        // The whole point: export verdicts, hand them back, re-judge them. The extra fields a
        // verdict carries (value, values, aliases) must be ignored rather than rejected.
        var payload = new AnnotationVerdictPayload
        {
            Profile = "CBBE 3BA",
            Category = "Belly",
            Rows =
            {
                new AnnotationVerdictRow
                {
                    Preset = "3BA Willendorf",
                    Gender = "Female",
                    Weight = 75,
                    Value = "Chubby",
                    Values = new List<string> { "Chubby" },
                    Aliases = new List<string> { "Willendorf (Outfit)" },
                },
            },
        };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        var cases = AnnotationCaseList.Parse(json, out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle();
        cases[0].PresetLabel.Should().Be("3BA Willendorf");
        cases[0].Weight.Should().Be(75);
        cases[0].Gender.Should().Be(Gender.Female);
    }

    [Fact]
    public void Json_FieldNamesAreCaseInsensitiveAndANumericWeightIsFine()
    {
        var cases = AnnotationCaseList.Parse(
            "[ { \"Preset\": \"A\", \"WEIGHT\": 50, \"Gender\": \"m\" } ]", out var warnings);

        warnings.Should().BeEmpty();
        cases[0].PresetLabel.Should().Be("A");
        cases[0].Weight.Should().Be(50);
        cases[0].Gender.Should().Be(Gender.Male);
    }

    [Fact]
    public void Json_MalformedInputWarnsInsteadOfThrowing()
    {
        var cases = AnnotationCaseList.Parse("{ not json at all", out var warnings);

        cases.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("Not valid JSON");
    }

    [Fact]
    public void Json_ObjectWithNoRecognizedArrayWarns()
    {
        var cases = AnnotationCaseList.Parse("{ \"category\": \"Belly\" }", out var warnings);

        cases.Should().BeEmpty();
        warnings.Should().ContainSingle().Which.Should().Contain("no array of cases");
    }

    // ---------- shared ----------

    [Fact]
    public void EmptyInputYieldsNoCasesAndNoWarnings()
    {
        AnnotationCaseList.Parse("   \n  \n", out var warnings).Should().BeEmpty();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void FormatIsDetectedFromTheFirstNonWhitespaceCharacter()
    {
        // Leading blank lines in front of JSON must not send it down the plain-text path, where
        // every line would become a preset name.
        var cases = AnnotationCaseList.Parse("\n\n  [ { \"preset\": \"A\" } ]", out var warnings);

        warnings.Should().BeEmpty();
        cases.Should().ContainSingle().Which.PresetLabel.Should().Be("A");
    }
}
