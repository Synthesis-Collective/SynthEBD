using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CharacterViewer.Rendering;

/// <summary>
/// Resolves shader source that ships with this assembly. The shaders are both
/// (a) copied next to the assembly (so local / <see cref="ProjectReference"/>
/// builds can hot-edit a <c>.frag</c> in the bin folder and re-run without a
/// rebuild) and (b) embedded into the assembly (so NuGet-package consumers — for
/// whom copied content does NOT flow through a PackageReference — and single-file
/// deployments still have the shaders). <see cref="ReadShaderSource"/> prefers
/// the on-disk copy and falls back to the embedded one.
/// </summary>
public static class ModuleResourceLocator
{
    /// <summary>
    /// Absolute path to the <c>Shaders/</c> directory shipped beside this
    /// assembly, or <c>null</c> when the assembly has no on-disk location
    /// (e.g. single-file deployment) — callers should then rely on the embedded
    /// shader copy via <see cref="ReadShaderSource"/>.
    /// </summary>
    public static string? ShaderDirectory
    {
        get
        {
            string? assemblyDir = Path.GetDirectoryName(typeof(ModuleResourceLocator).Assembly.Location);
            return string.IsNullOrEmpty(assemblyDir) ? null : Path.Combine(assemblyDir, "Shaders");
        }
    }

    /// <summary>
    /// Returns the GLSL source for a shader file (e.g. <c>"basic.frag"</c>).
    /// Prefers an on-disk copy under <paramref name="shaderDirectory"/> when one
    /// exists; otherwise reads the copy embedded in this assembly. Throws if the
    /// shader is found in neither place.
    /// </summary>
    public static string ReadShaderSource(string? shaderDirectory, string fileName)
    {
        // Prefer the on-disk copy: keeps the edit-in-bin-and-re-run iteration
        // workflow working for local builds, and matches the historical behavior
        // where shaders were loaded straight off disk.
        if (!string.IsNullOrEmpty(shaderDirectory))
        {
            string path = Path.Combine(shaderDirectory, fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        // Fall back to the embedded copy. Manifest resource names are
        // "<RootNamespace>.Shaders.<fileName>"; match by suffix so we are robust
        // to namespace/RootNamespace changes.
        var assembly = typeof(ModuleResourceLocator).Assembly;
        string suffix = ".Shaders." + fileName;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new InvalidOperationException(
                $"CharacterViewer.Rendering: shader '{fileName}' was not found on disk " +
                (shaderDirectory ?? "<no assembly location>") +
                " nor as an embedded resource. The package may be built without the " +
                "Shaders embedded-resource items.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
