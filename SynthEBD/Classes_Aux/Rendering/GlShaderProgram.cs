using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;

namespace SynthEBD;

/// <summary>
/// Compiles and links an OpenGL shader program from vertex + fragment source.
/// Caches uniform locations for fast access.
/// </summary>
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
    /// Loads shader source files from the Shaders directory next to this assembly.
    /// Falls back to embedded resources if files are not found.
    /// </summary>
    public static GlShaderProgram LoadFromFiles(string vertexPath, string fragmentPath)
    {
        string vertSrc = File.ReadAllText(vertexPath);
        string fragSrc = File.ReadAllText(fragmentPath);
        return new GlShaderProgram(vertSrc, fragSrc);
    }
}
