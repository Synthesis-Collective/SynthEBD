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

    // ---- vertex-edit layer (Option B) ----

    private static RegionVertexEdit Edit(float x, float y, float z, bool add) =>
        new RegionVertexEdit { X = x, Y = y, Z = z, Additive = add, IndexHint = -1 };

    [Fact]
    public void Region_WithVertexEdits_SurvivesJsonRoundTrip()
    {
        var profile = new BodyTypeProfile { Name = "RT", BodyTypeName = "CBBE 3BA" };
        var rg = ChestRegion();
        rg.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: true));
        rg.VertexEdits.Add(Edit(-2f, 100f, 0f, add: false));
        profile.Regions.Add(rg);

        var clone = JSONhandler<BodyTypeProfile>.CloneViaJSON(profile);
        clone.Should().NotBeNull();
        var rc = clone.Regions.Should().ContainSingle().Subject;
        rc.VertexEdits.Should().HaveCount(2);
        rc.VertexEdits[0].Additive.Should().BeTrue();
        rc.VertexEdits[0].X.Should().Be(1.5f);
        rc.VertexEdits[0].Z.Should().Be(-3f);
        rc.VertexEdits[1].Additive.Should().BeFalse();
        rc.VertexEdits[1].Y.Should().Be(100f);
    }

    [Fact]
    public void LegacyRegion_WithoutVertexEdits_DeserializesToEmptyList()
    {
        // A region JSON authored before this feature has no "VertexEdits" key at all → empty list,
        // which the resolver treats as a plain box (back-compat).
        const string legacy = "{ \"Name\": \"Old\", \"BodyTypeName\": \"CBBE 3BA\", \"Regions\": " +
            "[ { \"Name\": \"chest_bump\", \"ShapeName\": \"CBBE 3BA\", \"BoxMinX\": -8 } ] }";
        var profile = JSONhandler<BodyTypeProfile>.Deserialize(legacy, out bool ok, out string err);
        ok.Should().BeTrue(err);
        profile.Regions.Should().ContainSingle();
        profile.Regions[0].VertexEdits.Should().NotBeNull();
        profile.Regions[0].VertexEdits.Should().BeEmpty();
    }

    [Fact]
    public void Fingerprint_EmptyVertexEdits_MatchesPreFeatureBoxRegion()
    {
        // A box-only region (no edits) must fingerprint byte-identically to before the feature existed,
        // so upgrading the app does not invalidate every cached region volume.
        var withEmptyList = ChestRegion();           // VertexEdits defaults to an empty list
        var baseline = Fp(VolumeMeasurement(), withEmptyList);

        var rg = ChestRegion();
        rg.VertexEdits.Clear();                       // explicitly empty
        Fp(VolumeMeasurement(), rg).Should().Be(baseline);
    }

    [Fact]
    public void Fingerprint_ChangesWhenAVertexEditIsAdded()
    {
        var baseline = Fp(VolumeMeasurement(), ChestRegion());

        var edited = ChestRegion();
        edited.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: true));
        Fp(VolumeMeasurement(), edited).Should().NotBe(baseline);
    }

    [Fact]
    public void Fingerprint_IsOrderIndependent_ForVertexEdits()
    {
        var a = ChestRegion();
        a.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: true));
        a.VertexEdits.Add(Edit(-2f, 100f, 0f, add: false));

        var b = ChestRegion();
        b.VertexEdits.Add(Edit(-2f, 100f, 0f, add: false));   // reversed order
        b.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: true));

        Fp(VolumeMeasurement(), a).Should().Be(Fp(VolumeMeasurement(), b));
    }

    [Fact]
    public void Fingerprint_DistinguishesAddFromRemove_AtSamePosition()
    {
        var add = ChestRegion();
        add.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: true));

        var remove = ChestRegion();
        remove.VertexEdits.Add(Edit(1.5f, 95f, -3f, add: false));

        Fp(VolumeMeasurement(), add).Should().NotBe(Fp(VolumeMeasurement(), remove));
    }

    [Fact]
    public void Fingerprint_IgnoresIndexHint_ForVertexEdits()
    {
        // IndexHint is a non-authoritative cache, re-validated by position at resolve time, so it must
        // not enter the fingerprint (it can legitimately differ across sessions without an authoring change).
        var a = ChestRegion();
        a.VertexEdits.Add(new RegionVertexEdit { X = 1.5f, Y = 95f, Z = -3f, Additive = true, IndexHint = 1234 });

        var b = ChestRegion();
        b.VertexEdits.Add(new RegionVertexEdit { X = 1.5f, Y = 95f, Z = -3f, Additive = true, IndexHint = 9999 });

        Fp(VolumeMeasurement(), a).Should().Be(Fp(VolumeMeasurement(), b));
    }
}
