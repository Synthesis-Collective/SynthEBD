using System.IO;
using SkyRender;
using SkyRender.Character;
using FluentAssertions;
using Xunit;

namespace SkyRender.Tests;

/// <summary><see cref="BodyReplacement"/>: matching an NPC's resolved part paths to a body type's
/// slider-set outputs, and the value equality the viewer's same-NPC load short-circuit relies on.</summary>
public class BodyReplacementTests
{
    private static readonly Dictionary<string, string> Parts = new()
    {
        [BodyReplacement.PartKey(@"meshes\actors\character\character assets\femalehands")] = @"C:\bhunp\Hands.nif",
        [BodyReplacement.PartKey(@"meshes\actors\character\character assets\femalefeet")] = @"C:\bhunp\Feet.nif",
        [BodyReplacement.PartKey(@"meshes\actors\character\character assets\femalebody")] = @"C:\bhunp\Body Ver 4.nif",
    };

    [Theory]
    [InlineData(@"Actors\Character\Character Assets\FemaleHands_1.nif")]
    [InlineData(@"meshes/actors/character/character assets/femalehands_0.nif")]
    [InlineData(@"Data\Meshes\Actors\Character\Character Assets\FemaleHands.nif")]
    public void PartKey_AgreesAcrossPathForms(string resolved)
        => BodyReplacement.PartKey(resolved).Should().Be(@"actors\character\character assets\femalehands");

    [Fact]
    public void WithBodyReplacement_SwapsMatchingPartsAndKeepsTheRest()
    {
        var npc = new ResolvedNpcMeshPaths
        {
            BodyMeshPath = @"meshes\actors\character\character assets\femalebody_1.nif",
            HandsMeshPath = @"meshes\actors\character\character assets\femalehands_1.nif",
            FeetMeshPath = @"meshes\actors\character\character assets\femalefeet_1.nif",
            TailMeshPath = @"meshes\actors\character\character assets\tail.nif",
            HeadMeshPath = @"meshes\actors\character\facegendata\facegeom\skyrim.esm\0001.nif",
        };

        var swapped = npc.WithBodyReplacement(new BodyReplacement(@"C:\fallback\Body.nif", Parts));

        swapped.BodyMeshPath.Should().Be(@"C:\bhunp\Body Ver 4.nif");     // part map wins for the body path it names
        swapped.HandsMeshPath.Should().Be(@"C:\bhunp\Hands.nif");
        swapped.FeetMeshPath.Should().Be(@"C:\bhunp\Feet.nif");
        swapped.TailMeshPath.Should().Be(npc.TailMeshPath);               // not built by this body type: kept
        swapped.HeadMeshPath.Should().Be(npc.HeadMeshPath);
    }

    [Fact]
    public void WithBodyReplacement_UsesBodyNifForAnUnlistedBodyPath()
    {
        var npc = new ResolvedNpcMeshPaths { BodyMeshPath = @"meshes\actors\character\ygnord\femalebody_1.nif" };

        npc.WithBodyReplacement(new BodyReplacement(@"C:\bhunp\Body Ver 4.nif", Parts)).BodyMeshPath
            .Should().Be(@"C:\bhunp\Body Ver 4.nif");
    }

    private static NifMeshBuilder.BuiltMesh Face(uint shaderType, Dictionary<int, string> textures) => new()
    {
        Positions = Array.Empty<System.Numerics.Vector3>(),
        Normals = Array.Empty<System.Numerics.Vector3>(),
        Indices = Array.Empty<int>(),
        TextureCoordinates = Array.Empty<System.Numerics.Vector2>(),
        Tangents = Array.Empty<System.Numerics.Vector3>(),
        Bitangents = Array.Empty<System.Numerics.Vector3>(),
        ShapeName = "FemaleHeadBreton",
        ShaderType = shaderType,
        TexturePaths = textures,
    };

    private static Dictionary<int, string> VanillaHead() => new()
    {
        [0] = @"textures\actors\character\female\femalehead.dds",
        [1] = @"textures\actors\character\bretonfemale\femalehead_msn.dds",
        [3] = @"textures\actors\character\female\blankdetailmap.dds",
        [7] = @"textures\actors\character\female\femalehead_s.dds",
    };

    private static string SkinMod(params string[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "SkyRender_HeadSkin_" + Guid.NewGuid().ToString("N"));
        foreach (var f in files)
        {
            var full = Path.Combine(root, f);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        return root;
    }

    [Fact]
    public void HeadTextures_EverySlotTheSkinFolderHasIsSwapped_IncludingTheComplexionMap()
    {
        var mod = SkinMod(@"textures\actors\character\female\femalehead.dds",
            @"textures\actors\character\bretonfemale\femalehead_msn.dds",
            @"textures\actors\character\female\blankdetailmap.dds");
        var textures = VanillaHead();

        VM_CharacterViewer.ApplyReplacementHeadTextures(Face(4, textures), "Head", new BodyReplacement("b.nif", TextureFolder: mod), textures)
            .Should().BeTrue();

        textures[0].Should().StartWith(mod);
        textures[1].Should().StartWith(mod);
        textures[3].Should().StartWith(mod);                                               // detail / complexion slot too
        textures[7].Should().Be(@"textures\actors\character\female\femalehead_s.dds");     // not shipped: kept
        Directory.Delete(mod, recursive: true);
    }

    [Fact]
    public void HeadTextures_SlotsAreIndependent_AndNothingChangesWithNpcSkinOrANonFaceShape()
    {
        var mod = SkinMod(@"textures\actors\character\bretonfemale\femalehead_msn.dds");   // no femalehead.dds

        var textures = VanillaHead();
        VM_CharacterViewer.ApplyReplacementHeadTextures(Face(4, textures), "Head", new BodyReplacement("b.nif", TextureFolder: mod), textures)
            .Should().BeTrue();
        textures[0].Should().Be(VanillaHead()[0]);                                         // missing diffuse: NPC's kept
        textures[1].Should().StartWith(mod);

        var npcSkin = VanillaHead();
        VM_CharacterViewer.ApplyReplacementHeadTextures(Face(4, npcSkin), "Head",
            new BodyReplacement("b.nif", TextureFolder: mod, UsesNpcSkin: true), npcSkin).Should().BeFalse();

        var notFace = VanillaHead();
        VM_CharacterViewer.ApplyReplacementHeadTextures(Face(6, notFace), "Head", new BodyReplacement("b.nif", TextureFolder: mod), notFace)
            .Should().BeFalse();
        Directory.Delete(mod, recursive: true);
    }

    [Fact]
    public void Equality_ComparesPartMapContents()
    {
        var a = new BodyReplacement("b.nif", new Dictionary<string, string>(Parts), @"C:\skin", false);
        var b = new BodyReplacement("b.nif", new Dictionary<string, string>(Parts), @"C:\skin", false);

        a.Should().Be(b);
        a.Should().NotBe(a with { UsesNpcSkin = true });
        a.Should().NotBe(a with { TextureFolder = @"C:\other" });
        a.Should().NotBe(a with { PartMeshes = null });
    }
}
