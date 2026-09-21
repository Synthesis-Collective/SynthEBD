using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Pins <see cref="TextureVisibilityLock"/>, the pure state behind the character viewer's
/// "Lock Texture Visibility": which remembered choice reaches which newly created mesh. The OBody
/// annotation queue swaps preview NPCs on every weight change, so what matters is how a choice
/// made on one NPC's shapes carries to the next NPC's differently named ones, and that it never
/// touches a slot the mesh lacks. Every <see cref="GlMesh"/> here is built without a GL context:
/// the tests only read and write its flags, and never Upload or Dispose.
/// </summary>
public class TextureVisibilityLockTests
{
    private const TextureSlots BodySlots = TextureSlots.Diffuse | TextureSlots.Normal |
        TextureSlots.Skin | TextureSlots.Specular | TextureSlots.TintColor;
    private const TextureSlots FaceSlots = BodySlots | TextureSlots.FaceTint | TextureSlots.Detail;
    private const TextureSlots HairSlots = TextureSlots.Diffuse | TextureSlots.Normal |
        TextureSlots.Specular | TextureSlots.TintColor;
    private const TextureSlots EyeSlots = TextureSlots.Diffuse | TextureSlots.EnvMap | TextureSlots.Specular;
    private const TextureSlots OtherHeadSlots = TextureSlots.Diffuse | TextureSlots.Normal | TextureSlots.Specular;

    private static GlMesh Mesh(string bodyPart, string shapeName, TextureSlots present,
        bool skin = false, bool eye = false, bool hair = false) => new()
    {
        BodyPart = bodyPart,
        ShapeName = shapeName,
        IsSkinShape = skin,
        IsEye = eye,
        IsHairTintShader = hair,
        DiffuseTexture = present.HasFlag(TextureSlots.Diffuse) ? 1 : 0,
        HasNormalMap = present.HasFlag(TextureSlots.Normal),
        HasSkinMap = present.HasFlag(TextureSlots.Skin),
        HasSpecular = present.HasFlag(TextureSlots.Specular),
        HasFaceTintMap = present.HasFlag(TextureSlots.FaceTint),
        HasDetailMap = present.HasFlag(TextureSlots.Detail),
        HasEnvironmentMap = present.HasFlag(TextureSlots.EnvMap),
        HasEmissive = present.HasFlag(TextureSlots.Emissive),
        HasTintColor = present.HasFlag(TextureSlots.TintColor),
    };

    private static GlMesh Body(string name = "3BA") => Mesh("Body", name, BodySlots, skin: true);
    private static GlMesh Face(string name) => Mesh("Head", name, FaceSlots, skin: true);
    private static GlMesh Hair(string name) => Mesh("Head", name, HairSlots, hair: true);
    private static GlMesh Eyes(string name) => Mesh("Head", name, EyeSlots, eye: true);
    private static GlMesh HeadOther(string name) => Mesh("Head", name, OtherHeadSlots);

    [Theory]
    [InlineData(TextureSlots.Diffuse)]
    [InlineData(TextureSlots.Normal)]
    [InlineData(TextureSlots.Skin)]
    [InlineData(TextureSlots.Specular)]
    [InlineData(TextureSlots.FaceTint)]
    [InlineData(TextureSlots.Detail)]
    [InlineData(TextureSlots.EnvMap)]
    [InlineData(TextureSlots.Emissive)]
    [InlineData(TextureSlots.TintColor)]
    public void SlotAccessors_EachSlotMapsToExactlyItsOwnToggleAndPresenceFlag(TextureSlots slot)
    {
        var mesh = new GlMesh();
        mesh.DisabledTextureSlots().Should().Be(TextureSlots.None, "every toggle defaults to on");
        mesh.PresentTextureSlots().Should().Be(TextureSlots.None);

        mesh.SetTextureSlotsEnabled(slot, false);

        mesh.DisabledTextureSlots().Should().Be(slot);
        mesh.AreTextureSlotsEnabled(slot).Should().BeFalse();
        mesh.AreTextureSlotsEnabled(TextureSlots.All & ~slot).Should().BeTrue();
        Mesh("Body", "x", slot).PresentTextureSlots().Should().Be(slot);
    }

    [Fact]
    public void Apply_WithNothingRemembered_LeavesTheMeshAtItsDefaults()
    {
        var body = Body();

        new TextureVisibilityLock().Apply(body).Should().Be(TextureSlots.None);

        body.DisabledTextureSlots().Should().Be(TextureSlots.None);
    }

    [Fact]
    public void Record_OnTheBody_ReachesTheSameShapeAndADifferentlyNamedBodyOnTheNextNpc()
    {
        var hidden = TextureSlots.Diffuse | TextureSlots.Normal;
        var textureLock = new TextureVisibilityLock();
        textureLock.Record(Body("3BA"), hidden, enabled: false);

        var sameShape = Body("3BA");
        var otherBody = Body("CBBE");
        textureLock.Apply(sameShape).Should().Be(hidden);
        textureLock.Apply(otherBody).Should().Be(hidden, "the body group carries the choice across shape names");

        sameShape.DisabledTextureSlots().Should().Be(hidden);
        otherBody.DisabledTextureSlots().Should().Be(hidden);
    }

    [Fact]
    public void Record_OnTheFace_DoesNotReachTheNextNpcsHairEyesOrMouth()
    {
        // All four are tagged "Head" (one FaceGen NIF). A per-body-part rule would strip the
        // next NPC's hair and eyes because its face was hidden; the shape kind keeps them apart.
        var textureLock = new TextureVisibilityLock();
        textureLock.Record(Face("FemaleHeadNord"), TextureSlots.All, enabled: false);

        var face = Face("FemaleHeadImperial");
        var hair = Hair("HairFemaleImperial1");
        var eyes = Eyes("FemaleEyesHumanBrown");
        var mouth = HeadOther("MouthHumanoidDefault");
        foreach (var mesh in new[] { face, hair, eyes, mouth }) textureLock.Apply(mesh);

        face.DisabledTextureSlots().Should().Be(FaceSlots);
        hair.DisabledTextureSlots().Should().Be(TextureSlots.None);
        eyes.DisabledTextureSlots().Should().Be(TextureSlots.None);
        mouth.DisabledTextureSlots().Should().Be(TextureSlots.None);
    }

    [Fact]
    public void Apply_ResolvesEachSlotFromTheMostSpecificLevelThatDecidedIt()
    {
        var textureLock = new TextureVisibilityLock();
        // The user turned Diffuse back on for CBBE, then later hid Diffuse and Normal on 3BA.
        // The body group now says both are off, but CBBE's own choice keeps its Diffuse on.
        textureLock.Record(Body("CBBE"), TextureSlots.Diffuse, enabled: true);
        textureLock.Record(Body("3BA"), TextureSlots.Diffuse | TextureSlots.Normal, enabled: false);

        var cbbe = Body("CBBE");
        textureLock.Apply(cbbe);

        cbbe.DisabledTextureSlots().Should().Be(TextureSlots.Normal);
    }

    [Fact]
    public void Apply_NeverTouchesASlotTheMeshLacks()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.RecordAllHidden(new[] { Body() });

        // No diffuse texture and no normal map: Diffuse off would turn it flat grey, and a
        // missing slot's toggle must stay exactly as the mesh had it.
        var bare = Mesh("Body", "3BA", TextureSlots.Skin | TextureSlots.Specular, skin: true);
        textureLock.Apply(bare).Should().Be(TextureSlots.Skin | TextureSlots.Specular);

        bare.DiffuseEnabled.Should().BeTrue();
        bare.NormalEnabled.Should().BeTrue();
        bare.DisabledTextureSlots().Should().Be(TextureSlots.Skin | TextureSlots.Specular);
    }

    [Fact]
    public void Apply_OnlySetsTheSlotsItIsLimitedTo()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.RecordAllHidden(new[] { Body() });

        // The viewer passes this mask for a guest overlay, whose Translucent style draws its
        // overlay colour through Tint Color.
        var guestBody = Body();
        textureLock.Apply(guestBody, TextureSlots.All & ~TextureSlots.TintColor);

        guestBody.TintColorEnabled.Should().BeTrue();
        guestBody.DisabledTextureSlots().Should().Be(BodySlots & ~TextureSlots.TintColor);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.Record(Body(), TextureSlots.Diffuse, enabled: false);
        var body = Body();

        var first = textureLock.Apply(body);
        var stateAfterFirst = body.DisabledTextureSlots();
        var second = textureLock.Apply(body);

        second.Should().Be(first);
        body.DisabledTextureSlots().Should().Be(stateAfterFirst);
    }

    [Fact]
    public void Capture_KeepsEachShapesWholeState_AndGroupsLearnOnlyWhatWasHidden()
    {
        var body = Body("3BA");
        body.SetTextureSlotsEnabled(TextureSlots.Diffuse | TextureSlots.Normal, false);
        var mouth = HeadOther("MouthHumanoidDefault"); // left textured
        var brows = HeadOther("FemaleBrowsHuman06");   // same group as the mouth, Diffuse hidden
        brows.SetTextureSlotsEnabled(TextureSlots.Diffuse, false);

        var textureLock = new TextureVisibilityLock();
        textureLock.Capture(new[] { body, mouth, brows });

        var nextBody = Body("CBBE");
        var nextMouth = HeadOther("MouthHumanoidDefault");
        var nextBrows = HeadOther("FemaleBrowsHuman02");
        var nextHair = Hair("HairFemaleNord04");
        foreach (var mesh in new[] { nextBody, nextMouth, nextBrows, nextHair }) textureLock.Apply(mesh);

        nextBody.DisabledTextureSlots().Should().Be(TextureSlots.Diffuse | TextureSlots.Normal);
        nextMouth.DisabledTextureSlots().Should().Be(TextureSlots.None,
            "the same shape was textured when the lock engaged, which outranks its group");
        nextBrows.DisabledTextureSlots().Should().Be(TextureSlots.Diffuse, "its group had Diffuse hidden");
        nextHair.DisabledTextureSlots().Should().Be(TextureSlots.None,
            "a partly hidden scene sets no fallback for groups it didn't show");
    }

    [Fact]
    public void Capture_OfAFullyHiddenScene_AlsoHidesGroupsItDidNotShow()
    {
        var body = Body();
        var face = Face("FemaleHeadNord");
        body.SetTextureSlotsEnabled(TextureSlots.All, false);
        face.SetTextureSlotsEnabled(TextureSlots.All, false);

        var textureLock = new TextureVisibilityLock();
        textureLock.Capture(new[] { body, face });

        var wornWig = Mesh("Hair", "WigShape", HairSlots, hair: true);
        textureLock.Apply(wornWig);
        wornWig.DisabledTextureSlots().Should().Be(HairSlots);
    }

    [Fact]
    public void Capture_ReplacesWhateverWasRememberedBefore()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.RecordAllHidden(new[] { Body() });

        textureLock.Capture(new[] { Body() }); // a fully textured scene

        textureLock.HiddenSlots.Should().Be(TextureSlots.None);
        var body = Body("CBBE");
        textureLock.Apply(body);
        body.DisabledTextureSlots().Should().Be(TextureSlots.None);
    }

    [Fact]
    public void RecordAllHidden_CoversUnseenGroupsAndSlotsThatAppearLater()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.RecordAllHidden(new[] { Body() });

        var auxiliary = Mesh("Slot52", "Aux", BodySlots, skin: true);
        textureLock.Apply(auxiliary);
        auxiliary.DisabledTextureSlots().Should().Be(BodySlots, "the fallback covers a group the scene didn't have");

        // A texture override can give a mesh a normal map after it was created; the viewer
        // re-applies the lock to just the slots that appeared.
        var body = Mesh("Body", "3BA", BodySlots & ~TextureSlots.Normal, skin: true);
        textureLock.Apply(body);
        body.NormalEnabled.Should().BeTrue();
        body.HasNormalMap = true;
        textureLock.Apply(body, TextureSlots.Normal).Should().Be(TextureSlots.Normal);
        body.NormalEnabled.Should().BeFalse();
    }

    [Fact]
    public void Record_TurningASlotBackOn_IsRememberedAndClearsHiddenSlots()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.Record(Body(), TextureSlots.Diffuse, enabled: false);
        textureLock.HiddenSlots.Should().Be(TextureSlots.Diffuse);

        textureLock.Record(Body(), TextureSlots.Diffuse, enabled: true);

        textureLock.HiddenSlots.Should().Be(TextureSlots.None);
        var body = Body("CBBE");
        body.SetTextureSlotsEnabled(TextureSlots.Diffuse, false); // prove Apply turns it back on
        textureLock.Apply(body);
        body.DiffuseEnabled.Should().BeTrue();
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.RecordAllHidden(new[] { Body(), Face("FemaleHeadNord") });

        textureLock.Clear();

        textureLock.HiddenSlots.Should().Be(TextureSlots.None);
        var body = Body();
        textureLock.Apply(body).Should().Be(TextureSlots.None);
        body.DisabledTextureSlots().Should().Be(TextureSlots.None);
    }

    [Fact]
    public void ShapeMatch_IgnoresCaseAndSurroundingWhitespace()
    {
        var textureLock = new TextureVisibilityLock();
        textureLock.Record(Body("3BA"), TextureSlots.Diffuse, enabled: false);
        // Turn the body group's Diffuse back on, so only an exact shape match can hide it.
        textureLock.Record(Body("CBBE"), TextureSlots.Diffuse, enabled: true);

        var body = Body("3ba ");
        textureLock.Apply(body);

        body.DiffuseEnabled.Should().BeFalse();
    }
}
