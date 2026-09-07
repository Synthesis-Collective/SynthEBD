using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Guards <see cref="MeasurementCacheStore.ComputeBodyMeshHash(IReadOnlyDictionary{string,int}, BodyTypeProfile)"/>,
/// the body-mesh validation behind the measurement cache.
///
/// The hash previously ran over <c>viewer.GetCurrentShapeVertexCounts()</c> verbatim, which returns
/// every renderable mesh — head, hands, hair, whatever outfit the preview NPC wears. That made the
/// cache valid only while the *same preview NPC* was loaded: switching NPCs mismatched the hash and
/// wiped 25k+ cached rows even though the measured body mesh was byte-identical. The hash must
/// depend only on the shapes the profile actually reads geometry from.
/// </summary>
public class MeasurementCacheBodyMeshHashTests
{
    private static BodyTypeProfile ProfileMeasuring(params string[] shapeNames)
    {
        var p = new BodyTypeProfile();
        foreach (var n in shapeNames)
        {
            p.KeyVertices.Add(new NamedKeyVertex { Name = "kv_" + n, ShapeName = n });
        }
        return p;
    }

    [Fact]
    public void ExtraViewerShapes_DoNotChangeTheHash()
    {
        var profile = ProfileMeasuring("3BA");
        var bodyOnly = new Dictionary<string, int> { ["3BA"] = 18436 };
        var wholeNpc = new Dictionary<string, int>
        {
            ["3BA"] = 18436,
            ["Head"] = 4023,      // different preview NPC = different head/hands/hair/outfit
            ["Hands"] = 1112,
            ["HairLong"] = 7781,
        };

        MeasurementCacheStore.ComputeBodyMeshHash(wholeNpc, profile)
            .Should().Be(MeasurementCacheStore.ComputeBodyMeshHash(bodyOnly, profile),
                "the preview NPC's other meshes have no bearing on the measured body");
    }

    [Fact]
    public void MeasuredShapeVertexCountChange_ChangesTheHash()
    {
        var profile = ProfileMeasuring("3BA");
        var a = new Dictionary<string, int> { ["3BA"] = 18436 };
        var b = new Dictionary<string, int> { ["3BA"] = 12740 };   // e.g. plain CBBE

        MeasurementCacheStore.ComputeBodyMeshHash(a, profile)
            .Should().NotBe(MeasurementCacheStore.ComputeBodyMeshHash(b, profile),
                "a real body-mesh swap must still invalidate the cache");
    }

    [Fact]
    public void MeasuredShapeAbsent_ReturnsEmptySoCallersSkipValidation()
    {
        var profile = ProfileMeasuring("3BA");
        var noBody = new Dictionary<string, int> { ["Head"] = 4023 };

        MeasurementCacheStore.ComputeBodyMeshHash(noBody, profile).Should().BeEmpty();
        MeasurementCacheStore.ComputeBodyMeshHash(new Dictionary<string, int>(), profile).Should().BeEmpty();
        MeasurementCacheStore.ComputeBodyMeshHash(null, profile).Should().BeEmpty();
    }

    [Fact]
    public void ProfileWithNoMeasuredShapes_ReturnsEmpty()
    {
        MeasurementCacheStore.ComputeBodyMeshHash(
            new Dictionary<string, int> { ["3BA"] = 18436 }, new BodyTypeProfile()).Should().BeEmpty();
        MeasurementCacheStore.ComputeBodyMeshHash(
            new Dictionary<string, int> { ["3BA"] = 18436 }, null).Should().BeEmpty();
    }

    [Fact]
    public void ViewerCasingDrift_DoesNotChangeTheHash()
    {
        var profile = ProfileMeasuring("3BA");
        var upper = new Dictionary<string, int> { ["3BA"] = 18436 };
        var lower = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase) { ["3ba"] = 18436 };

        MeasurementCacheStore.ComputeBodyMeshHash(lower, profile)
            .Should().Be(MeasurementCacheStore.ComputeBodyMeshHash(upper, profile),
                "keys come from the profile's authored names, not the viewer's");
    }

    [Fact]
    public void GetMeasuredShapeNames_CoversKeyVerticesAndRegions()
    {
        var p = ProfileMeasuring("3BA");
        p.Regions.Add(new NamedRegion { Name = "Bust", ShapeName = "3BA" });
        p.Regions.Add(new NamedRegion { Name = "Other", ShapeName = "SomeOtherShape" });
        p.KeyVertices.Add(new NamedKeyVertex { Name = "blank", ShapeName = "" });

        MeasurementCacheStore.GetMeasuredShapeNames(p)
            .Should().BeEquivalentTo(new[] { "3BA", "SomeOtherShape" });
    }

    [Fact]
    public void RegionOnlyProfile_StillHashes()
    {
        var p = new BodyTypeProfile();
        p.Regions.Add(new NamedRegion { Name = "Bust", ShapeName = "3BA" });

        MeasurementCacheStore.ComputeBodyMeshHash(
            new Dictionary<string, int> { ["3BA"] = 18436, ["Head"] = 1 }, p)
            .Should().Be(MeasurementCacheStore.ComputeBodyMeshHash(
                new Dictionary<string, int> { ["3BA"] = 18436 }, p));
    }
}
