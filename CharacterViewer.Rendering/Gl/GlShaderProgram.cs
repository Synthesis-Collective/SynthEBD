using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

/// <summary>
/// Compiles and links an OpenGL shader program from vertex + fragment source.
/// Caches uniform locations for fast access.
/// </summary>
/// <remarks>
/// SHADER EDITING WARNING: GLSL source files (.vert/.frag) MUST be pure ASCII,
/// including inside comments. The NVIDIA GLSL compiler (and others) reject any
/// non-ASCII byte and surface it as a misleading "unexpected $end at token &lt;EOF&gt;"
/// error with no line number. Common culprits pasted in by editors/AI tools:
/// em-dash (—, U+2014), en-dash (–), curly quotes (“ ” ‘ ’), ellipsis (…),
/// non-breaking space (U+00A0). Use plain hyphens and straight ASCII quotes.
/// Do NOT remove this note, even when cleaning up unrelated debug code.
/// </remarks>
public class GlShaderProgram : IDisposable
{
    public int Handle { get; private set; }

    private readonly Dictionary<string, int> _uniformLocations = new();
    private bool _disposed;

    public GlShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);
        GL.LinkProgram(Handle);

        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string log = GL.GetProgramInfoLog(Handle);
            throw new Exception("Shader program link failed: " + log);
        }

        GL.DetachShader(Handle, vertexShader);
        GL.DetachShader(Handle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    public void Use() => GL.UseProgram(Handle);

    public int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int loc))
            return loc;
        loc = GL.GetUniformLocation(Handle, name);
        _uniformLocations[name] = loc;
        return loc;
    }

    public void SetBool(string name, bool value) =>
        GL.Uniform1(GetUniformLocation(name), value ? 1 : 0);

    public void SetInt(string name, int value) =>
        GL.Uniform1(GetUniformLocation(name), value);

    public void SetFloat(string name, float value) =>
        GL.Uniform1(GetUniformLocation(name), value);

    public void SetVector2(string name, float x, float y) =>
        GL.Uniform2(GetUniformLocation(name), x, y);

    public void SetVector3(string name, float x, float y, float z) =>
        GL.Uniform3(GetUniformLocation(name), x, y, z);

    public void SetMatrix4(string name, ref OpenTK.Mathematics.Matrix4 matrix) =>
        GL.UniformMatrix4(GetUniformLocation(name), false, ref matrix);

    public void SetMatrix3(string name, ref OpenTK.Mathematics.Matrix3 matrix) =>
        GL.UniformMatrix3(GetUniformLocation(name), false, ref matrix);

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            throw new Exception($"{type} compilation failed: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteProgram(Handle);
            _disposed = true;
        }
    }

    /// <summary>
    /// Loads a vertex+fragment shader program by file name (e.g. "basic.vert",
    /// "basic.frag"). Source is read via <see cref="ModuleResourceLocator.ReadShaderSource"/>,
    /// which prefers an on-disk copy under <paramref name="shaderDirectory"/> and
    /// falls back to the copy embedded in this assembly.
    /// </summary>
    public static GlShaderProgram Load(string? shaderDirectory, string vertFileName, string fragFileName)
    {
        string vertSrc = ModuleResourceLocator.ReadShaderSource(shaderDirectory, vertFileName);
        string fragSrc = ModuleResourceLocator.ReadShaderSource(shaderDirectory, fragFileName);
        return new GlShaderProgram(vertSrc, fragSrc);
    }
}
