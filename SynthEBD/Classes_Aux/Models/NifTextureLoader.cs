using System;
using System.Collections.Generic;
using System.IO;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;

namespace SynthEBD;

/// <summary>
/// Loads DDS textures from game assets and applies them to HelixToolkit
/// PhongMaterial on mesh models. HelixToolkit natively supports DDS streams,
/// so no intermediate conversion (e.g. via Pfim) is needed.
/// </summary>
public class NifTextureLoader
{
    private readonly GameAssetResolver _assetResolver;
    private readonly Logger _logger;

    public NifTextureLoader(GameAssetResolver assetResolver, Logger logger)
    {
        _assetResolver = assetResolver;
        _logger = logger;
    }

    /// <summary>
    /// Applies textures from a NIF shape's BSShaderTextureSet to a model's PhongMaterial.
    /// <paramref name="texturePaths"/> is keyed by slot index (0=diffuse, 1=normal, 7=specular).
    /// </summary>
    public void ApplyTexturesToModel(MeshGeometryModel3D model, Dictionary<int, string> texturePaths)
    {
        if (model.Material is not PhongMaterial material)
        {
            return;
        }

        foreach (var kvp in texturePaths)
        {
            int slot = kvp.Key;
            string gamePath = kvp.Value;

            var texture = LoadDdsTexture(gamePath);
            if (texture == null)
            {
                continue;
            }

            switch (slot)
            {
                case 0:
                    material.DiffuseMap = texture;
                    break;
                case 1:
                    material.NormalMap = texture;
                    break;
                case 7:
                    material.SpecularColorMap = texture;
                    break;
                default:
                    // Other slots (2=glow/subsurface, etc.) not mapped to PhongMaterial properties
                    break;
            }
        }
    }

    /// <summary>
    /// Applies texture overrides from <see cref="FilePathReplacement"/> entries
    /// (as used in ForcedSubgroups) to the appropriate mesh model and texture slot.
    /// </summary>
    /// <param name="modelsByBodyPart">Maps body part name ("Head", "Body", "Hands", "Feet") to its model.</param>
    /// <param name="overrides">FilePathReplacement entries with Source (texture path) and Destination (slot descriptor).</param>
    public void ApplyTextureOverrides(
        Dictionary<string, MeshGeometryModel3D> modelsByBodyPart,
        IEnumerable<FilePathReplacement> overrides)
    {
        foreach (var replacement in overrides)
        {
            string dest = replacement.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(replacement.Source))
            {
                continue;
            }

            string? bodyPart = ParseBodyPart(dest);
            int? slot = ParseTextureSlot(dest);

            if (bodyPart == null || slot == null)
            {
                continue;
            }

            if (!modelsByBodyPart.TryGetValue(bodyPart, out var model))
            {
                continue;
            }

            var texture = LoadDdsTexture(replacement.Source);
            if (texture == null)
            {
                continue;
            }

            if (model.Material is not PhongMaterial material)
            {
                continue;
            }

            switch (slot.Value)
            {
                case 0:
                    material.DiffuseMap = texture;
                    break;
                case 1:
                    material.NormalMap = texture;
                    break;
                case 7:
                    material.SpecularColorMap = texture;
                    break;
            }

            _logger.LogMessage("CharacterViewer: Override texture '" + replacement.Source +
                "' -> dest '" + dest + "' (slot " + slot.Value + " on " + bodyPart + ")");
        }
    }

    /// <summary>
    /// Loads a DDS texture from a game-relative path and wraps it in a
    /// HelixToolkit <see cref="TextureModel"/>. Returns null on failure.
    /// </summary>
    public TextureModel? LoadDdsTexture(string relativeGamePath)
    {
        string? resolved = _assetResolver.ResolveAssetPath(relativeGamePath);
        if (resolved == null)
        {
            _logger.LogMessage("CharacterViewer: Texture not found: '" + relativeGamePath + "'");
            return null;
        }

        try
        {
            // HelixToolkit's TextureModel natively supports DDS streams.
            // We must keep the stream open (autoCloseStream: false) because
            // the GPU reads it lazily during rendering.
            var stream = File.OpenRead(resolved);
            var texture = new TextureModel(stream, false);

            _logger.LogMessage("CharacterViewer: Loaded texture from '" + relativeGamePath + "'");
            return texture;
        }
        catch (Exception ex)
        {
            _logger.LogError("CharacterViewer: Failed to load texture '" + relativeGamePath + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Parses a FilePathDestinationMap destination string to determine the target body part.
    /// </summary>
    private static string? ParseBodyPart(string destination)
    {
        if (destination.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
        {
            return "Head";
        }

        if (destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase) ||
            destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase))
        {
            if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase))
                return "Body";
            if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase))
                return "Hands";
            if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase))
                return "Feet";
        }

        return null;
    }

    /// <summary>
    /// Parses a FilePathDestinationMap destination string to determine the texture slot index.
    /// </summary>
    private static int? ParseTextureSlot(string destination)
    {
        // Check from most specific to least to avoid substring false matches
        if (destination.Contains("BacklightMaskOrSpecular", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (destination.Contains("NormalOrGloss", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (destination.Contains("GlowOrDetailMap", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (destination.Contains("Diffuse", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (destination.Contains("Height", StringComparison.OrdinalIgnoreCase))
            return 3;

        return null;
    }
}
