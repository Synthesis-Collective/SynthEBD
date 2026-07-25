using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CharacterViewer.Rendering;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SynthEBD.Tests;

/// <summary>
/// Guards the GL-resource teardown contract in <see cref="GlRenderer"/> — the leak fixed in commit
/// 253d42a9 ("Fix per-render GL resource leak in GlRenderer.Dispose"), where Dispose deleted only 5 of
/// the GL objects Initialize/EnsureShadowFbo/EnsureSsaoFbos create. A fresh GlRenderer is built and
/// disposed for every offscreen mugshot, so each render orphaned driver-side memory — unmanaged and
/// invisible to the GC, which is why it showed up as a monotonic *native* climb during batch mugshot
/// generation with no managed-heap symptom to profile against.
///
/// <para>That fix left the invariant stated only as a comment in Dispose ("Keep this symmetric with the
/// resource set enumerated in ForgetResourcesFromDeadContext()"). Since then the renderer has gained the
/// slider-morph heatmap VAO/VBO and the bloom program + three ping-pong FBOs, and each time the author
/// remembered to extend both teardown paths — by discipline, with nothing enforcing it. These tests turn
/// the comment into an assertion: every GL handle field must be (a) reset by
/// ForgetResourcesFromDeadContext and (b) named in the Dispose teardown region. Add an FBO, texture, VAO
/// or program and forget either half, and one of these fails.</para>
///
/// <para>Neither test needs a GL context. <see cref="GlRenderer.ForgetResourcesFromDeadContext"/> is pure
/// field assignment so it can be driven directly; Dispose cannot (it issues real GL delete calls), so its
/// half is checked against the source text instead.</para>
/// </summary>
public class GlRendererDisposalSymmetryTests
{
    private readonly ITestOutputHelper _out;

    public GlRendererDisposalSymmetryTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Every GL handle the renderer owns is either an <c>int</c> field (an FBO / texture / VAO / VBO
    /// name) or a <see cref="GlShaderProgram"/> field (a linked program). The <c>_</c> prefix filter
    /// matters: GlRenderer also has public <c>int</c> auto-properties (SkinTintOperator, FaceTintMode,
    /// …) whose compiler-generated backing fields would otherwise be mistaken for GL handles. Consts
    /// (ShadowMapSize, SsaoKernelSize) are static literals and never appear here.
    /// </summary>
    private static FieldInfo[] HandleFields() => typeof(GlRenderer)
        .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(f => f.Name.StartsWith('_')
                    && (f.FieldType == typeof(int) || f.FieldType == typeof(GlShaderProgram)))
        .OrderBy(f => f.Name, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void ForgetResourcesFromDeadContext_ResetsEveryGlHandleField()
    {
        // Build a GlRenderer without running its constructor (which would need a live GL context), then
        // dirty every handle so any field the method forgets to reset stands out against the sentinel.
        var renderer = (GlRenderer)RuntimeHelpers.GetUninitializedObject(typeof(GlRenderer));

        // ForgetResourcesFromDeadContext opens with _meshes.Clear(); the readonly collection fields are
        // null on an uninitialized instance, so give them real instances first.
        foreach (var listField in typeof(GlRenderer)
                     .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(f => f.FieldType.IsGenericType &&
                                 f.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            listField.SetValue(renderer, Activator.CreateInstance(listField.FieldType));
        }

        const int sentinel = 424242;
        var fields = HandleFields();
        fields.Should().NotBeEmpty(
            "GlRenderer owns GL handles — a reflection filter that matches none is broken, and would make " +
            "this whole test vacuously pass");

        foreach (var f in fields)
        {
            f.SetValue(renderer, f.FieldType == typeof(int)
                ? sentinel
                // A GlShaderProgram instance without running its ctor: enough to prove the field is nulled.
                : RuntimeHelpers.GetUninitializedObject(typeof(GlShaderProgram)));
        }

        renderer.ForgetResourcesFromDeadContext();

        var notReset = fields
            .Where(f => f.FieldType == typeof(int)
                ? (int)f.GetValue(renderer)! == sentinel
                : f.GetValue(renderer) is not null)
            .Select(f => f.Name)
            .ToArray();

        _out.WriteLine($"Checked {fields.Length} GL handle fields.");
        notReset.Should().BeEmpty(
            "ForgetResourcesFromDeadContext must drop every GL handle when the context dies — a field it " +
            "misses keeps a stale handle that a later call can delete out from under a new context");
    }

    [Fact]
    public void Dispose_TearsDownEveryGlHandleField()
    {
        if (!TryReadRendererSource(out var source, out var skipReason))
        {
            _out.WriteLine("SKIP: " + skipReason);
            return;
        }

        // Dispose delegates the viewport-sized FBO sets to these two helpers, so the teardown region is
        // all three bodies taken together.
        var teardown = new StringBuilder();
        foreach (var signature in new[]
                 {
                     "public void Dispose()",
                     "private void DestroySsaoFbos()",
                     "private void DestroyBloomFbos()",
                 })
        {
            TryExtractMethodBody(source!, signature, out var body)
                .Should().BeTrue($"GlRenderer.cs should still contain '{signature}'");
            teardown.AppendLine(body);
        }

        var teardownText = teardown.ToString();
        var missing = HandleFields()
            .Select(f => f.Name)
            .Where(name => !teardownText.Contains(name, StringComparison.Ordinal))
            .ToArray();

        missing.Should().BeEmpty(
            "every GL handle must be deleted in Dispose (directly or via DestroySsaoFbos/DestroyBloomFbos). " +
            "A handle created per render and never deleted leaks unmanaged driver memory the GC cannot see — " +
            "the regression fixed in 253d42a9. Missing: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Locates <c>CharacterViewer.Rendering/Gl/GlRenderer.cs</c> relative to this test file's own
    /// compile-time path (this file sits in <c>SynthEBD.Tests/</c>, one level below the repo root).
    /// Graceful-skips rather than failing if the source tree moved after the build, matching the
    /// suite's convention for environment-dependent tests.
    /// </summary>
    private static bool TryReadRendererSource(out string? source, out string? skipReason,
        [CallerFilePath] string thisFile = "")
    {
        source = null;
        skipReason = null;

        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(thisFile));
        var path = repoRoot is null
            ? null
            : Path.Combine(repoRoot, "CharacterViewer.Rendering", "Gl", "GlRenderer.cs");

        if (path is null || !File.Exists(path))
        {
            skipReason = $"GlRenderer.cs not found at '{path}' — source tree moved since build.";
            return false;
        }

        source = File.ReadAllText(path);
        return true;
    }

    /// <summary>
    /// Returns the text of the method whose signature line contains <paramref name="signature"/>, from
    /// its opening brace to the matching close. Plain brace counting is sufficient for the three
    /// teardown methods — they contain no braces inside strings, chars, or comments.
    /// </summary>
    private static bool TryExtractMethodBody(string source, string signature, out string body)
    {
        body = string.Empty;

        int at = source.IndexOf(signature, StringComparison.Ordinal);
        if (at < 0) return false;

        int open = source.IndexOf('{', at);
        if (open < 0) return false;

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                body = source[open..(i + 1)];
                return true;
            }
        }

        return false;
    }
}
