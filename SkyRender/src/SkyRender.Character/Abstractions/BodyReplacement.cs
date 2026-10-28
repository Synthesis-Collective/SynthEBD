using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyRender.Character;

/// <summary>
/// Host-chosen replacement of an NPC's body meshes and body skin, applied at load time by
/// <see cref="VM_CharacterViewer.BodyReplacement"/> (and per guest by <see cref="VM_CharacterViewer.LoadGuestAsync"/>).
/// Lets a host preview a body type that isn't the installed one -- a host's morph-tool Compare window
/// fills it from a body type's morph-tool slider sets.
/// </summary>
/// <param name="BodyNifPath">Absolute disk path of the body NIF, always swapped in for the NPC's body.</param>
/// <param name="PartMeshes">Further swaps by game path: <see cref="PartKey"/> of a mesh path a body mod
/// writes (e.g. <c>meshes\actors\character\character assets\femalehands</c>) -> absolute NIF path. Each of
/// the NPC's resolved hands / feet / tail meshes is replaced when its key is here, so they match the body
/// (a slider set's <c>OutputPath\OutputFile</c> is exactly such a key). A body key here wins over
/// <see cref="BodyNifPath"/> for a body path it names. Null/absent key = keep the NPC's mesh.</param>
/// <param name="TextureFolder">Where the replacement skin's textures live: a data-folder-like root under
/// which each replaced-or-kept body-part skin shape's NIF texture paths are looked up
/// (<see cref="VM_CharacterViewer.ResolveTextureUnderFolder"/>). A skin shape whose diffuse isn't there
/// renders neutral beige. Ignored with <see cref="UsesNpcSkin"/>.</param>
/// <param name="UsesNpcSkin">Keep the NPC's own skin TXST and tint on every body part, as on the worn body
/// -- for a replacement that shares the NPC's body's UV layout.</param>
public sealed record BodyReplacement(
    string BodyNifPath,
    IReadOnlyDictionary<string, string>? PartMeshes = null,
    string? TextureFolder = null,
    bool UsesNpcSkin = false)
{
    /// <summary>The body parts whose skin follows the replacement: their skin shapes (ShaderType 5) take
    /// <see cref="TextureFolder"/>'s textures, or beige, so the whole body wears one skin with no seam at
    /// the wrists or ankles. The FaceGen head's face skin also takes <see cref="TextureFolder"/>'s head
    /// textures, slot by slot, wherever that folder has the file a slot asks for (complexion map included;
    /// the FaceGen tint stays the NPC's).</summary>
    public static readonly IReadOnlySet<string> SkinBodyParts =
        new HashSet<string>(StringComparer.Ordinal) { "Body", "Hands", "Feet", "Tail" };

    /// <summary>
    /// The lookup key for a mesh path: lower-case, backslashes, no leading <c>data\</c> or <c>meshes\</c>,
    /// no extension and no <c>_0</c>/<c>_1</c> weight suffix -- so a resolved
    /// <c>Meshes/Actors/Character/Character Assets/FemaleHands_1.nif</c> and a slider set's
    /// <c>meshes\actors\character\character assets</c> + <c>femalehands</c> agree.
    /// </summary>
    public static string PartKey(string path)
    {
        var p = (path ?? "").Replace('/', '\\').Trim().TrimStart('\\').ToLowerInvariant();
        if (p.StartsWith("data\\", StringComparison.Ordinal)) p = p.Substring(5);
        if (p.StartsWith("meshes\\", StringComparison.Ordinal)) p = p.Substring(7);
        if (p.EndsWith(".nif", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 4);
        if (p.EndsWith("_0", StringComparison.Ordinal) || p.EndsWith("_1", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 2);
        return p;
    }

    /// <summary>The replacement for one resolved part path, or null to keep it.</summary>
    public string? FindPartMesh(string? resolvedPath)
        => string.IsNullOrWhiteSpace(resolvedPath) || PartMeshes == null ? null
            : PartMeshes.TryGetValue(PartKey(resolvedPath), out var nif) ? nif : null;

    /// <summary>A string identifying everything that changes the built scene, for cache keys.</summary>
    public string CacheKey => string.Join("|", BodyNifPath, TextureFolder ?? "", UsesNpcSkin,
        string.Join(";", (PartMeshes ?? new Dictionary<string, string>()).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key + "=" + kv.Value)));

    /// <summary>Value equality including <see cref="PartMeshes"/>' contents (records compare it by reference).</summary>
    public bool Equals(BodyReplacement? other) => other != null && CacheKey == other.CacheKey;

    public override int GetHashCode() => CacheKey.GetHashCode(StringComparison.Ordinal);
}
