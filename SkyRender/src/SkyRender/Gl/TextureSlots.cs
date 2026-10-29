using System;
using System.Collections.Generic;

namespace SkyRender;

/// <summary>
/// The nine per-shape texture toggles a <see cref="GlMesh"/> carries — its <c>*Enabled</c>
/// flags, pushed to basic.frag as the <c>u_enable*</c> uniforms — as flags, so a set of slots
/// is a single value. Names follow the character viewer's right-click menu.
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
    /// The slots this mesh actually has — the ones a host's slot menu should offer.
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
