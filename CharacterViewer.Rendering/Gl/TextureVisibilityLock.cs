using System;
using System.Collections.Generic;

namespace CharacterViewer.Rendering;

/// <summary>
/// The nine per-shape texture toggles a <see cref="GlMesh"/> carries — its <c>*Enabled</c>
/// flags, pushed to basic.frag as the <c>u_enable*</c> uniforms — as flags, so a set of slots
/// is a single value. Names follow the viewer's right-click menu.
/// </summary>
[Flags]
public enum TextureSlots
{
    None      = 0,
    Diffuse   = 1 << 0,
    Normal    = 1 << 1,
    Skin      = 1 << 2,
    Specular  = 1 << 3,
    FaceTint  = 1 << 4,
    Detail    = 1 << 5,
    EnvMap    = 1 << 6,
    Emissive  = 1 << 7,
    TintColor = 1 << 8,
    All = Diffuse | Normal | Skin | Specular | FaceTint | Detail | EnvMap | Emissive | TintColor,
}

/// <summary>Reads and writes a <see cref="GlMesh"/>'s texture toggles by <see cref="TextureSlots"/>.</summary>
public static class GlMeshTextureSlotExtensions
{
    /// <summary>
    /// The slots this mesh actually has — the ones the viewer's right-click menu offers.
    /// Toggling a slot the mesh lacks is either invisible (the shader also gates on the
    /// matching <c>has_*</c> flag) or wrong: Diffuse off draws flat grey whether or not the
    /// shape had a diffuse to hide.
    /// </summary>
    public static TextureSlots PresentTextureSlots(this GlMesh mesh)
    {
        var slots = TextureSlots.None;
        if (mesh.DiffuseTexture != 0) slots |= TextureSlots.Diffuse;
        if (mesh.HasNormalMap)        slots |= TextureSlots.Normal;
        if (mesh.HasSkinMap)          slots |= TextureSlots.Skin;
        if (mesh.HasSpecular)         slots |= TextureSlots.Specular;
        if (mesh.HasFaceTintMap)      slots |= TextureSlots.FaceTint;
        if (mesh.HasDetailMap)        slots |= TextureSlots.Detail;
        if (mesh.HasEnvironmentMap)   slots |= TextureSlots.EnvMap;
        if (mesh.HasEmissive)         slots |= TextureSlots.Emissive;
        if (mesh.HasTintColor)        slots |= TextureSlots.TintColor;
        return slots;
    }

    /// <summary>The slots whose toggle is off, whether or not the mesh has them.</summary>
    public static TextureSlots DisabledTextureSlots(this GlMesh mesh)
    {
        var slots = TextureSlots.None;
        if (!mesh.DiffuseEnabled)   slots |= TextureSlots.Diffuse;
        if (!mesh.NormalEnabled)    slots |= TextureSlots.Normal;
        if (!mesh.SkinEnabled)      slots |= TextureSlots.Skin;
        if (!mesh.SpecularEnabled)  slots |= TextureSlots.Specular;
        if (!mesh.FaceTintEnabled)  slots |= TextureSlots.FaceTint;
        if (!mesh.DetailEnabled)    slots |= TextureSlots.Detail;
        if (!mesh.EnvMapEnabled)    slots |= TextureSlots.EnvMap;
        if (!mesh.EmissiveEnabled)  slots |= TextureSlots.Emissive;
        if (!mesh.TintColorEnabled) slots |= TextureSlots.TintColor;
        return slots;
    }

    /// <summary>True when every slot in <paramref name="slots"/> is switched on.</summary>
    public static bool AreTextureSlotsEnabled(this GlMesh mesh, TextureSlots slots)
        => (mesh.DisabledTextureSlots() & slots) == TextureSlots.None;

    /// <summary>Switches every slot in <paramref name="slots"/> on or off and leaves the rest alone.</summary>
    public static void SetTextureSlotsEnabled(this GlMesh mesh, TextureSlots slots, bool enabled)
    {
        if (slots.HasFlag(TextureSlots.Diffuse))   mesh.DiffuseEnabled = enabled;
        if (slots.HasFlag(TextureSlots.Normal))    mesh.NormalEnabled = enabled;
        if (slots.HasFlag(TextureSlots.Skin))      mesh.SkinEnabled = enabled;
        if (slots.HasFlag(TextureSlots.Specular))  mesh.SpecularEnabled = enabled;
        if (slots.HasFlag(TextureSlots.FaceTint))  mesh.FaceTintEnabled = enabled;
        if (slots.HasFlag(TextureSlots.Detail))    mesh.DetailEnabled = enabled;
        if (slots.HasFlag(TextureSlots.EnvMap))    mesh.EnvMapEnabled = enabled;
        if (slots.HasFlag(TextureSlots.Emissive))  mesh.EmissiveEnabled = enabled;
        if (slots.HasFlag(TextureSlots.TintColor)) mesh.TintColorEnabled = enabled;
    }
}

/// <summary>
/// Remembers which texture slots the user switched off in the character viewer, so the choice
/// can be re-applied to meshes the viewer creates later — another NPC, another weight's
/// preview NPC, a head-only rebuild, a mesh override, a guest overlay. Pure state with no GL or
/// WPF dependency: <see cref="VM_CharacterViewer"/> owns one instance, decides when it is
/// engaged, and calls <see cref="Apply"/> on each new mesh.
///
/// <para><b>Keys.</b> Shape names are not stable across NPCs (the body is usually the same
/// shape everywhere; head, hair, hands and feet differ), so a choice is remembered at three
/// levels and resolved per slot, most specific first:</para>
/// <list type="number">
///   <item><b>Shape</b> — body part + shape name: exactly the mesh the choice was made on.</item>
///   <item><b>Group</b> — body part + shape kind (skin / eye / hair / other). The kind matters
///     because one body part can hold very different shapes: a FaceGen head is the face, eyes,
///     hair and brows, all tagged <c>Head</c>, and hiding one NPC's face must not strip the next
///     NPC's hair.</item>
///   <item><b>Fallback</b> — for shapes whose group was never seen. Set only when everything was
///     hidden (<see cref="RecordAllHidden"/>, or a fully hidden scene at <see cref="Capture"/>),
///     so "hide it all" also holds for a part the first NPC didn't have.</item>
/// </list>
/// <para>A slot no level decided keeps the mesh's own value, and a slot the mesh lacks
/// (<see cref="GlMeshTextureSlotExtensions.PresentTextureSlots"/>) is never touched.</para>
/// </summary>
public sealed class TextureVisibilityLock
{
    /// <summary>One level's remembered choice: the slots it decided, and which of those are off.</summary>
    private readonly record struct SlotChoice(TextureSlots Decided, TextureSlots Disabled)
    {
        public SlotChoice With(TextureSlots slots, bool enabled) =>
            new(Decided | slots, enabled ? Disabled & ~slots : Disabled | slots);

        /// <summary>This choice, with <paramref name="lessSpecific"/> filling only the slots it
        /// left undecided.</summary>
        public SlotChoice Then(SlotChoice lessSpecific) =>
            new(Decided | lessSpecific.Decided, Disabled | (lessSpecific.Disabled & ~Decided));
    }

    private static readonly SlotChoice AllHidden = new(TextureSlots.All, TextureSlots.All);

    private readonly Dictionary<string, SlotChoice> _byShape = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SlotChoice> _byGroup = new(StringComparer.OrdinalIgnoreCase);
    private SlotChoice _fallback;

    /// <summary>Every slot that some level remembers as hidden. Non-empty means a mesh created
    /// now may come up with textures off.</summary>
    public TextureSlots HiddenSlots
    {
        get
        {
            var hidden = _fallback.Disabled;
            foreach (var choice in _byShape.Values) hidden |= choice.Disabled;
            foreach (var choice in _byGroup.Values) hidden |= choice.Disabled;
            return hidden;
        }
    }

    /// <summary>Forgets everything; meshes created afterwards keep their defaults.</summary>
    public void Clear()
    {
        _byShape.Clear();
        _byGroup.Clear();
        _fallback = default;
    }

    /// <summary>
    /// Replaces the remembered state with the choices <paramref name="meshes"/> show right now —
    /// the moment the lock engages. Each shape keeps its whole state, on and off, so the same
    /// shape on the next NPC looks the way it does now. Its group learns only what is off: on is
    /// every new mesh's default anyway, and recording it would let an untouched shape cancel a
    /// sibling the user did hide. A scene with every slot off is what "hide everything" leaves
    /// behind, so it sets the fallback as well.
    /// </summary>
    public void Capture(IEnumerable<GlMesh> meshes)
    {
        Clear();
        bool anyTextured = false;
        bool allHidden = true;
        foreach (var mesh in meshes)
        {
            var present = mesh.PresentTextureSlots();
            if (present == TextureSlots.None) continue;
            anyTextured = true;

            var off = mesh.DisabledTextureSlots() & present;
            if (off != present) allHidden = false;

            string shapeKey = ShapeKey(mesh);
            _byShape[shapeKey] = Lookup(_byShape, shapeKey)
                .With(present & ~off, enabled: true)
                .With(off, enabled: false);
            if (off != TextureSlots.None)
                Remember(_byGroup, GroupKey(mesh), off, enabled: false);
        }
        if (anyTextured && allHidden) _fallback = AllHidden;
    }

    /// <summary>Records the user switching <paramref name="slots"/> on or off on one mesh, for
    /// that exact shape and for its group. The latest choice wins.</summary>
    public void Record(GlMesh mesh, TextureSlots slots, bool enabled)
    {
        if (slots == TextureSlots.None) return;
        Remember(_byShape, ShapeKey(mesh), slots, enabled);
        Remember(_byGroup, GroupKey(mesh), slots, enabled);
    }

    /// <summary>Records "hide everything": every slot off for each of <paramref name="meshes"/>
    /// and their groups — absent slots included, so a map a later texture override adds stays
    /// hidden too — plus the fallback, for groups none of them belong to.</summary>
    public void RecordAllHidden(IEnumerable<GlMesh> meshes)
    {
        foreach (var mesh in meshes) Record(mesh, TextureSlots.All, enabled: false);
        _fallback = AllHidden;
    }

    /// <summary>
    /// Applies the remembered choice to <paramref name="mesh"/>, limited to
    /// <paramref name="limitTo"/> and to the slots the mesh has. Idempotent. Returns the slots
    /// it set, on or off — <see cref="TextureSlots.None"/> when nothing remembered applied.
    /// </summary>
    public TextureSlots Apply(GlMesh mesh, TextureSlots limitTo = TextureSlots.All)
    {
        var choice = Lookup(_byShape, ShapeKey(mesh))
            .Then(Lookup(_byGroup, GroupKey(mesh)))
            .Then(_fallback);
        var applied = choice.Decided & mesh.PresentTextureSlots() & limitTo;
        mesh.SetTextureSlotsEnabled(applied & ~choice.Disabled, true);
        mesh.SetTextureSlotsEnabled(applied & choice.Disabled, false);
        return applied;
    }

    /// <summary>
    /// The kind that tells apart the very different shapes one body part can hold, read from
    /// the shader flags <c>ApplyTexturesToGlMesh</c> sets: skin is body, hands, feet and face
    /// (shader types 4/5); a FaceGen head's hair and usually its brows are hair-tint; eyes are
    /// eyes; everything else (mouth, lashes, attire material) is "other".
    /// </summary>
    internal static string ShapeKind(GlMesh mesh) =>
        mesh.IsSkinShape ? "skin" : mesh.IsEye ? "eye" : mesh.IsHairTintShader ? "hair" : "other";

    private static string ShapeKey(GlMesh mesh) =>
        mesh.BodyPart + "|" + (mesh.ShapeName ?? string.Empty).Trim();

    private static string GroupKey(GlMesh mesh) => mesh.BodyPart + "|" + ShapeKind(mesh);

    private static SlotChoice Lookup(Dictionary<string, SlotChoice> map, string key) =>
        map.TryGetValue(key, out var choice) ? choice : default;

    private static void Remember(Dictionary<string, SlotChoice> map, string key,
        TextureSlots slots, bool enabled) =>
        map[key] = Lookup(map, key).With(slots, enabled);
}
