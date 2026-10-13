using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// <see cref="BodySlideMeasurementEvaluator.Evaluate"/> resolves regions itself when a caller passes none
/// (the BodySlide-setting live classifier, the annotation table). <see cref="BodySlideMeasurementEvaluator.NeedsResolvedRegions"/>
/// decides when that is necessary.
/// </summary>
public class EvaluatorRegionResolutionTests
{
    private static BodyTypeProfile Profile(bool region, bool regionVolume, bool regionKeyVertex) => new()
    {
        Regions = region ? new List<NamedRegion> { new() { Name = "Band", ShapeName = "3BA" } } : new List<NamedRegion>(),
        Measurements = new List<MeasurementDefinition>
        {
            new() { Name = "d", Kind = MeasurementKind.AxisDistance, VertexRefNames = new() { "A", "B" } },
            regionVolume ? new() { Name = "v", Kind = MeasurementKind.RegionVolume, RegionRefName = "Band" } : null!,
        },
        KeyVertices = new List<NamedKeyVertex>
        {
            new() { Name = "A", Strategy = KeyVertexStrategy.BoundingBox },
            regionKeyVertex ? new() { Name = "R", Strategy = KeyVertexStrategy.Region, RegionRefName = "Band" } : null!,
        },
    };

    [Fact]
    public void RegionVolumeMeasurement_NeedsRegions() =>
        BodySlideMeasurementEvaluator.NeedsResolvedRegions(Profile(true, true, false)).Should().BeTrue();

    [Fact]
    public void RegionStrategyKeyVertex_NeedsRegions() =>
        BodySlideMeasurementEvaluator.NeedsResolvedRegions(Profile(true, false, true)).Should().BeTrue();

    [Fact]
    public void DistanceOnlyProfile_DoesNotResolve() =>
        BodySlideMeasurementEvaluator.NeedsResolvedRegions(Profile(true, false, false)).Should().BeFalse();

    [Fact]
    public void NoRegionsDefined_DoesNotResolve() =>
        BodySlideMeasurementEvaluator.NeedsResolvedRegions(Profile(false, true, true)).Should().BeFalse();

    [Fact]
    public void NullProfile_DoesNotResolve() =>
        BodySlideMeasurementEvaluator.NeedsResolvedRegions(null).Should().BeFalse();
}
