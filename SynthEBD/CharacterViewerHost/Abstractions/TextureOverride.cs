namespace SynthEBD;

/// <summary>
/// Neutral texture-replacement record that the viewer rendering tier accepts in
/// place of SynthEBD's host-coupled <see cref="FilePathReplacement"/>. The host
/// is responsible for converting from its own override representation before
/// calling the viewer; this keeps the viewer's public API free of host types.
///
/// <see cref="GameRelativePath"/> is a Skyrim-style asset path
/// (e.g. "textures\\actors\\character\\female\\femalebody_1.dds") that the
/// rendering tier will resolve via <see cref="GameAssetResolver"/>.
/// </summary>
public readonly record struct TextureOverride(string BodyPart, int Slot, string GameRelativePath);
