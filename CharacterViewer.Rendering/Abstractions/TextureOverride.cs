namespace CharacterViewer.Rendering;

/// <summary>
/// Neutral texture-replacement record that the viewer rendering tier accepts in
/// place of SynthEBD's host-coupled <see cref="FilePathReplacement"/>. The host
/// is responsible for converting from its own override representation before
/// calling the viewer; this keeps the viewer's public API free of host types.
///
/// <see cref="GameRelativePath"/> is a Skyrim-style asset path
/// (e.g. "textures\\actors\\character\\female\\femalebody_1.dds") that the
/// rendering tier will resolve via <see cref="GameAssetResolver"/>.
///
/// <para><see cref="ShapeName"/> is an optional per-shape qualifier. When null
/// (the common case) the override targets every skin shape of <see cref="BodyPart"/>
/// — a plain skin retexture. When set, it targets ONLY the shape whose NIF geometry
/// node name equals it, leaving the body part's other shapes untouched. This is how a
/// worn-armor <c>AlternateTextures</c> (MODS) entry retextures a single named sub-shape
/// (a body replacer may split a shared body NIF into several distinctly-named shapes and
/// retexture just one of them): the host resolves the alternate texture's <c>Name</c> —
/// the target 3D object name (e.g. <c>"BodyShapeB"</c>) — from the record
/// template and passes it here so the renderer matches it against
/// <c>GlMesh.ShapeName</c>. Unlike the mesh-override <c>ShapeTextures</c> channel, this
/// applies to the base body, which is rendered directly rather than as a mesh override.</para>
/// </summary>
public readonly record struct TextureOverride(string BodyPart, int Slot, string GameRelativePath, string? ShapeName = null);
