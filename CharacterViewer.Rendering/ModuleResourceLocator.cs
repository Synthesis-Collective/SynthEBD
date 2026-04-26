using System;
using System.IO;
using System.Reflection;

namespace CharacterViewer.Rendering;

/// <summary>
/// Resolves on-disk locations for resources that ship next to this assembly
/// (currently shaders only). Used by the GL renderer's initialization path so
/// the same code works whether SynthEBD or NPC Plugin Chooser 2 is the host —
/// in both cases the assembly's <see cref="Assembly.Location"/> is in the
/// host's bin folder, and <see cref="ProjectReference"/> copy-content rules
/// place <c>Shaders/</c> beside it.
/// </summary>
public static class ModuleResourceLocator
{
    /// <summary>
    /// Absolute path to the <c>Shaders/</c> directory shipped beside this
    /// assembly. Throws if the assembly was loaded without a file location
    /// (e.g. single-file deployment) — that would need a separate
    /// embedded-resource-based loader.
    /// </summary>
    public static string ShaderDirectory
    {
        get
        {
            string? assemblyDir = Path.GetDirectoryName(typeof(ModuleResourceLocator).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDir))
            {
                throw new InvalidOperationException(
                    "CharacterViewer.Rendering: assembly has no on-disk location; " +
                    "shaders cannot be located. Single-file deployment is not yet supported.");
            }
            return Path.Combine(assemblyDir, "Shaders");
        }
    }
}
