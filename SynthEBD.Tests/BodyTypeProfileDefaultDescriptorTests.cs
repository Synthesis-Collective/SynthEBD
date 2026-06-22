using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Tests for the per-Category "default value" fallback emitted after the classifier rule pass by
/// <see cref="BodySlideMeasurementEvaluator.ComputeDefaultDescriptors"/>. The helper is the shared
/// core used by both <see cref="BodySlideMeasurementEvaluator.Evaluate"/> and the editor's
/// cache-rederive path (<c>VM_BodyTypeProfile.DeriveDescriptorsFor</c>), so exercising it directly
/// pins the semantics both paths inherit: a Category falls into its default only when no rule
/// produced a descriptor for it.
/// </summary>
public class BodyTypeProfileDefaultDescriptorTests
{
    private static (string Category, string Value)[] Matched(params (string, string)[] m) => m;

    [Fact]
    public void NoDefaultsConfigured_EmitsNothing()
    {
        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(new Dictionary<string, string>(), Matched())
            .ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void NullDefaults_EmitsNothing()
    {
        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(null!, Matched(("BodyShape", "Athletic")))
            .ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void CategoryWithNoRuleMatch_EmitsDefault()
    {
        var defaults = new Dictionary<string, string> { ["BodyShape"] = "Average" };

        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(defaults, Matched())
            .ToList();

        result.Should().ContainSingle();
        result[0].Category.Should().Be("BodyShape");
        result[0].Value.Should().Be("Average");
    }

    [Fact]
    public void CategoryAlreadyMatchedByRule_SuppressesDefault()
    {
        var defaults = new Dictionary<string, string> { ["BodyShape"] = "Average" };

        // A rule already produced BodyShape:Athletic, so the BodyShape Category is covered —
        // the default must not also fire (otherwise a preset would carry two BodyShape values).
        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(defaults, Matched(("BodyShape", "Athletic")))
            .ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void MixedCategories_OnlyUnmatchedFallIntoDefault()
    {
        var defaults = new Dictionary<string, string>
        {
            ["BodyShape"] = "Average",  // matched below -> suppressed
            ["Tone"] = "Soft",          // unmatched -> emitted
            ["Build"] = "Normal",       // unmatched -> emitted
        };

        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(defaults, Matched(("BodyShape", "Curvy")))
            .ToList();

        result.Select(r => (r.Category, r.Value)).Should().BeEquivalentTo(new[]
        {
            ("Tone", "Soft"),
            ("Build", "Normal"),
        });
    }

    [Fact]
    public void BlankCategoryOrValue_IsSkipped()
    {
        var defaults = new Dictionary<string, string>
        {
            [""] = "Average",     // blank category
            ["Tone"] = "",        // blank value
            ["Build"] = "Normal", // valid
        };

        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(defaults, Matched())
            .ToList();

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Build");
        result[0].Value.Should().Be("Normal");
    }

    [Fact]
    public void CategoryMatchIsCaseSensitive_DifferentCaseStillFallsIntoDefault()
    {
        // Descriptor categories are compared Ordinal throughout the classifier (see the Ordinal
        // HashSet in ComputeDefaultDescriptors), so a differently-cased "match" does NOT cover the
        // configured-case Category — the default still fires. Pins the Ordinal contract.
        var defaults = new Dictionary<string, string> { ["BodyShape"] = "Average" };

        var result = BodySlideMeasurementEvaluator
            .ComputeDefaultDescriptors(defaults, Matched(("bodyshape", "Athletic")))
            .ToList();

        result.Should().ContainSingle();
        result[0].Value.Should().Be("Average");
    }
}
