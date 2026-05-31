using System.Collections.Generic;
using FluentAssertions;
using SynthEBD;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Phase-2 model/fingerprint tests for the RegionVolume measurement: that a <see cref="NamedRegion"/>
/// plus a <see cref="MeasurementKind.RegionVolume"/> measurement survive the real JSON persistence
/// path, and that <see cref="MeasurementCacheStore.ComputeMeasurementFingerprint(MeasurementDefinition, System.Collections.Generic.IReadOnlyDictionary{string, NamedKeyVertex}, System.Collections.Generic.IReadOnlyDictionary{string, NamedRegion})"/>
/// derives a RegionVolume measurement's identity from (shape, box, expected cap count) only.
/// </summary>
public class RegionVolumeModelTests
{
    private static NamedRegion ChestRegion() => new NamedRegion
    {
        Name = "chest_bump",
        ShapeName = "CBBE 3BA",
        BoxMinX = -8f, BoxMinY = 90f, BoxMinZ = -12f,
        BoxMaxX = 8f, BoxMaxY = 110f, BoxMaxZ = 2f,
        ExpectedCapCount = 1,
    };

    private static MeasurementDefinition VolumeMeasurement(string regionName = "chest_bump") => new MeasurementDefinition
    {
        Name = "chest_volume",
        Kind = MeasurementKind.RegionVolume,
        RegionRefName = regionName,
    };

    private static IReadOnlyDictionary<string, NamedRegion> RegionMap(params NamedRegion[] regions)
    {
        var d = new Dictionary<string, NamedRegion>();
        foreach (var r in regions) d[r.Name] = r;
        return d;
    }

    private static IReadOnlyDictionary<string, NamedKeyVertex> EmptyKvMap()
        => new Dictionary<string, NamedKeyVertex>();

    private static string Fp(MeasurementDefinition def, params NamedRegion[] regions)
        => MeasurementCacheStore.ComputeMeasurementFingerprint(def, EmptyKvMap(), RegionMap(regions));

    // ---- serialization ----

    [Fact]
    public void Profile_WithRegionAndVolumeMeasurement_SurvivesJsonRoundTrip()
    {
        var profile = new BodyTypeProfile { Name = "RT", BodyTypeName = "CBBE 3BA" };
        profile.Regions.Add(ChestRegion());
        profile.Measurements.Add(VolumeMeasurement());

        var json = JSONhandler<BodyTypeProfile>.Serialize(profile, out bool ok, out string err);
        ok.Should().BeTrue(err);
        json.Should().Contain("\"RegionVolume\"");   // enum persisted as name via StringEnumConverter
        json.Should().Contain("chest_bump");

        var clone = JSONhandler<BodyTypeProfile>.Deserialize(json, out bool ok2, out string err2);
        ok2.Should().BeTrue(err2);
        clone.Should().NotBeNull();

        clone.Regions.Should().HaveCount(1);
        var rg = clone.Regions[0];
        rg.Name.Should().Be("chest_bump");
        rg.ShapeName.Should().Be("CBBE 3BA");
        rg.BoxMinX.Should().Be(-8f);
        rg.BoxMaxY.Should().Be(110f);
        rg.ExpectedCapCount.Should().Be(1);

        clone.Measurements.Should().HaveCount(1);
        var m = clone.Measurements[0];
        m.Kind.Should().Be(MeasurementKind.RegionVolume);
        m.RegionRefName.Should().Be("chest_bump");
    }

    [Fact]
    public void ExpectedCapCountNull_RoundTripsAsNull()
    {
        var profile = new BodyTypeProfile { Name = "RT", BodyTypeName = "CBBE 3BA" };
        var rg = ChestRegion();
        rg.ExpectedCapCount = null;
        profile.Regions.Add(rg);

        var clone = JSONhandler<BodyTypeProfile>.CloneViaJSON(profile);
        clone.Should().NotBeNull();
        clone.Regions.Should().HaveCount(1);
        clone.Regions[0].ExpectedCapCount.Should().BeNull();
    }

    [Fact]
    public void LegacyProfile_WithoutRegions_DeserializesToEmptyList()
    {
        // A profile JSON authored before this feature has no "Regions" key at all.
        const string legacy = "{ \"Name\": \"Old\", \"BodyTypeName\": \"CBBE 3BA\", \"Measurements\": [] }";
        var profile = JSONhandler<BodyTypeProfile>.Deserialize(legacy, out bool ok, out string err);
        ok.Should().BeTrue(err);
        profile.Should().NotBeNull();
        profile.Regions.Should().NotBeNull();
        profile.Regions.Should().BeEmpty();
    }

    // ---- fingerprint identity ----

    [Fact]
    public void Fingerprint_IsStableForIdenticalRegion()
    {
        var a = Fp(VolumeMeasurement(), ChestRegion());
        var b = Fp(VolumeMeasurement(), ChestRegion());
        a.Should().Be(b);
    }

    [Fact]
    public void Fingerprint_ChangesWhenBoxCoordChanges()
    {
        var baseline = Fp(VolumeMeasurement(), ChestRegion());

        var moved = ChestRegion();
        moved.BoxMaxZ += 0.5f;
        Fp(VolumeMeasurement(), moved).Should().NotBe(baseline);
    }

    [Fact]
    public void Fingerprint_ChangesWhenExpectedCapCountChanges()
    {
        var baseline = Fp(VolumeMeasurement(), ChestRegion());

        var twoCaps = ChestRegion();
        twoCaps.ExpectedCapCount = 2;
        Fp(VolumeMeasurement(), twoCaps).Should().NotBe(baseline);

        var anyCaps = ChestRegion();
        anyCaps.ExpectedCapCount = null;
        Fp(VolumeMeasurement(), anyCaps).Should().NotBe(baseline);
    }

    [Fact]
    public void Fingerprint_ChangesWhenShapeNameChanges()
    {
        var baseline = Fp(VolumeMeasurement(), ChestRegion());

        var other = ChestRegion();
        other.ShapeName = "BHUNP";
        Fp(VolumeMeasurement(), other).Should().NotBe(baseline);
    }

    [Fact]
    public void Fingerprint_IgnoresVertexRefNames_ForRegionVolume()
    {
        // RegionVolume reads RegionRefName, not VertexRefNames, so polluting the vertex list
        // must not perturb the fingerprint — proves the kind switched onto the region branch.
        var bare = VolumeMeasurement();
        var withVerts = VolumeMeasurement();
        withVerts.VertexRefNames = new List<string> { "ignored_a", "ignored_b" };

        Fp(withVerts, ChestRegion()).Should().Be(Fp(bare, ChestRegion()));
    }

    [Fact]
    public void Fingerprint_MissingRegion_DiffersFromPresent_AndDoesNotThrow()
    {
        var present = Fp(VolumeMeasurement(), ChestRegion());
        var missing = Fp(VolumeMeasurement() /* no region supplied */);
        missing.Should().NotBeNullOrEmpty();
        missing.Should().NotBe(present);
    }

    [Fact]
    public void Fingerprint_DistinguishesRegionVolumeFromDistance_SameName()
    {
        // Same measurement name, different kind => different identity.
        var vol = new MeasurementDefinition { Name = "x", Kind = MeasurementKind.RegionVolume, RegionRefName = "chest_bump" };
        var dist = new MeasurementDefinition { Name = "x", Kind = MeasurementKind.PointDistance, VertexRefNames = new List<string> { "a", "b" } };

        Fp(vol, ChestRegion()).Should().NotBe(Fp(dist, ChestRegion()));
    }
}
