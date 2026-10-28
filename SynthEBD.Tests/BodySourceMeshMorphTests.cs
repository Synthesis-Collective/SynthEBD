using System.IO;
using System.Numerics;
using System.Xml.Linq;
using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Slider data for a BodySlide source mesh, as the Compare window feeds it to the deformer: the
/// slider-set reader (both formats), the per-target regrouping in <see cref="SynthEbdOsdLoader.LoadFiles"/>,
/// and the deformer's shape matching (exact name beats substring; canonical names skip prefix stripping).
/// </summary>
public class BodySourceMeshMorphTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SynthEBD_MorphTest_" + Guid.NewGuid().ToString("N"));

    public BodySourceMeshMorphTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class NullLogger : ICharacterViewerLogger
    {
        public void LogMessage(string message) { }
        public void LogError(string message) { }
        public void LogError(string message, Exception ex) { }
    }

    private static BsdFileParser Parser() => new(new NullLogger(), new CharacterViewerLogGate());

    /// <summary>A UUNP-style .bsd: uint32 count, then (uint32 index, float x, y, z) records.</summary>
    private string WriteBsd(string name, params (uint Index, float X)[] deltas)
    {
        var path = Path.Combine(_dir, name + ".bsd");
        using var w = new BinaryWriter(File.Create(path));
        w.Write((uint)deltas.Length);
        foreach (var (index, x) in deltas) { w.Write(index); w.Write(x); w.Write(0f); w.Write(0f); }
        return path;
    }

    private static OsdFile Osd(string shape, bool canonical, params (string Name, ushort Index)[] sliders) => new()
    {
        ShapeName = shape,
        SliderNamesAreCanonical = canonical,
        Sliders = sliders.Select(s => new OsdSliderData
        {
            Name = s.Name,
            VertexDeltas = new Dictionary<ushort, Vector3> { [s.Index] = Vector3.UnitX },
        }).ToList(),
    };

    [Fact]
    public void ReadSliderData_CurrentFormat_SplitsEntriesByTargetShape()
    {
        File.WriteAllText(Path.Combine(_dir, "Body v2.osd"), "");
        var set = XElement.Parse("""
            <SliderSet name="Body"><DataFolder>X</DataFolder>
              <Slider name="AnkleSize">
                <Data name="3BAAnkleSize" target="3BA" local="true">Body v2.osd\3BAAnkleSize</Data>
              </Slider>
              <Slider name="BigButt">
                <Data name="3BABigButt" target="3BA" local="true">Body v2.osd\3BABigButt</Data>
                <Data name="3BA_AnusBigButt" target="3BA_Anus" local="true">Body v2.osd\3BA_AnusBigButt</Data>
              </Slider>
              <Slider name="Missing"><Data name="m" target="3BA">Other.osd\m</Data></Slider>
            </SliderSet>
            """);

        var data = BodyTypeSourceMeshes.ReadSliderData(set, _dir);

        data.Select(d => (d.EntryName, d.TargetShape, d.SliderName)).Should().BeEquivalentTo(new[]
        {
            ("3BAAnkleSize", "3BA", "AnkleSize"),
            ("3BABigButt", "3BA", "BigButt"),
            ("3BA_AnusBigButt", "3BA_Anus", "BigButt"),
        });
    }

    [Fact]
    public void ReadSliderData_OlderFormat_ReadsPerSliderBsdFiles()
    {
        WriteBsd("Breasts", (1, 1f));
        var set = XElement.Parse("""
            <SliderSet name="Unified UNP"><SetFolder>Unified UNP</SetFolder>
              <Slider name="Breasts"><datafile name="Breasts" target="UUNP">Breasts.bsd</datafile></Slider>
            </SliderSet>
            """);

        var data = BodyTypeSourceMeshes.ReadSliderData(set, _dir);

        data.Should().ContainSingle().Which.Should().Match<BodyMorphData>(d =>
            d.TargetShape == "UUNP" && d.SliderName == "Breasts" && d.FilePath.EndsWith("Breasts.bsd"));
    }

    [Fact]
    public void LoadFiles_RetargetsBsdSlidersToTheirMeshShape_AndCaches()
    {
        var breasts = WriteBsd("Breasts", (1, 1f));
        var butt = WriteBsd("Butt", (2, 2f));
        var files = new BodyMorphFiles(new[]
        {
            new BodyMorphData(breasts, "Breasts", "UUNP", "Breasts"),
            new BodyMorphData(butt, "Butt", "UUNP", "Butt"),
        }, Array.Empty<string>());
        var loader = new SynthEbdOsdLoader(null!, null!, Parser());

        var loaded = loader.LoadFiles(files);

        loaded.Should().ContainSingle();
        loaded[0].ShapeName.Should().Be("UUNP");
        loaded[0].SliderNamesAreCanonical.Should().BeTrue();
        loaded[0].Sliders.Select(s => s.Name).Should().BeEquivalentTo("Breasts", "Butt");
        loader.LoadFiles(files).Should().BeSameAs(loaded);

        // ...so the deformer, filtering by the mesh shape, actually finds them.
        BodySlideDeformer.BuildSliderDeltaMap(loaded, "UUNP").Keys.Should().BeEquivalentTo("Breasts", "Butt");
    }

    [Fact]
    public void BuildSliderDeltaMap_ExactShapeMatchBeatsSubstring()
    {
        var torso = Osd("3BA", true, ("BigButt", 100));
        var anus = Osd("3BA_Anus", true, ("BigButt", 5));
        var files = new List<OsdFile> { torso, anus };

        // "3BA" is a substring of "3BA_Anus": without the exact-match rule both would apply.
        BodySlideDeformer.BuildSliderDeltaMap(files, "3BA_Anus")["BigButt"].Keys.Should().Equal((ushort)5);
        BodySlideDeformer.BuildSliderDeltaMap(files, "3BA")["BigButt"].Keys.Should().Equal((ushort)100);
    }

    [Fact]
    public void BuildSliderDeltaMap_SubstringStillMatchesWhenNothingIsExact()
    {
        // A file-stem OSD name, as the registry path produces.
        var stem = Osd("CBBE 3BA Ref", false, ("3BA RefAnkleSize", 1), ("3BA RefBigButt", 2));

        BodySlideDeformer.BuildSliderDeltaMap(new List<OsdFile> { stem }, "3BA").Keys
            .Should().BeEquivalentTo("AnkleSize", "BigButt");
    }

    [Fact]
    public void ResolveTextureUnderFolder_AcceptsPathsWithAndWithoutTexturesPrefix()
    {
        var tex = Path.Combine(_dir, "textures", "actors", "character", "female", "femalebody_1.dds");
        Directory.CreateDirectory(Path.GetDirectoryName(tex)!);
        File.WriteAllText(tex, "x");

        VM_CharacterViewer.ResolveTextureUnderFolder(_dir, @"textures\actors\character\female\FemaleBody_1.dds").Should().NotBeNull();
        VM_CharacterViewer.ResolveTextureUnderFolder(_dir, @"actors/character/female/femalebody_1.dds").Should().NotBeNull();
        VM_CharacterViewer.ResolveTextureUnderFolder(_dir, @"textures\actors\character\female\malebody_1.dds").Should().BeNull();
        VM_CharacterViewer.ResolveTextureUnderFolder(null, @"textures\x.dds").Should().BeNull();
    }

    [Fact]
    public void BuildSliderDeltaMap_CanonicalNamesKeepTheirSharedPrefix()
    {
        // Without the flag, the common prefix "Vagina" would be stripped as if it were a shape tag.
        var small = Osd("3BA_Vagina", true, ("VaginaSize", 1), ("VaginaHole", 2));

        BodySlideDeformer.BuildSliderDeltaMap(new List<OsdFile> { small }, "3BA_Vagina").Keys
            .Should().BeEquivalentTo("VaginaSize", "VaginaHole");
    }
}
