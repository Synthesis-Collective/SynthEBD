using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace CharacterViewer.Rendering;

/// <summary>Context-local capability checks and upload. Caller owns the returned handle.</summary>
internal sealed class GlCompressedTexture
{
    private readonly HashSet<string> _extensions = new(StringComparer.Ordinal);
    private readonly int _major, _minor, _maxSize;

    internal GlCompressedTexture()
    {
        _major = GL.GetInteger(GetPName.MajorVersion);
        _minor = GL.GetInteger(GetPName.MinorVersion);
        _maxSize = GL.GetInteger(GetPName.MaxTextureSize);
        int count = GL.GetInteger(GetPName.NumExtensions);
        for (int i = 0; i < count; i++) _extensions.Add(GL.GetString(StringNameIndexed.Extensions, i));
    }

    internal bool Supports(CompressedDds dds)
    {
        if (dds.Width > _maxSize || dds.Height > _maxSize) return false;
        return dds.Format switch
        {
            DdsBlockFormat.Bc1 or DdsBlockFormat.Bc2 or DdsBlockFormat.Bc3 =>
                _extensions.Contains("GL_EXT_texture_compression_s3tc"),
            DdsBlockFormat.Bc4 or DdsBlockFormat.Bc5 => _major >= 3 || _extensions.Contains("GL_ARB_texture_compression_rgtc"),
            DdsBlockFormat.Bc7 => _major > 4 || (_major == 4 && _minor >= 2) || _extensions.Contains("GL_ARB_texture_compression_bptc"),
            _ => false
        };
    }

    internal int Upload(CompressedDds dds, out long storedBytes)
    {
        // Linear formats intentionally ignore the sRGB *label*: the existing
        // shader performs its own color conversion, and normal maps use raw values.
        var format = (InternalFormat)(dds.Format switch
        {
            DdsBlockFormat.Bc1 => 0x83F1, DdsBlockFormat.Bc2 => 0x83F2,
            DdsBlockFormat.Bc3 => 0x83F3, DdsBlockFormat.Bc4 => 0x8DBB,
            DdsBlockFormat.Bc5 => 0x8DBD, DdsBlockFormat.Bc7 => 0x8E8C,
            _ => throw new ArgumentOutOfRangeException(nameof(dds))
        });
        int handle = GL.GenTexture();
        storedBytes = 0;
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, handle);
            var pin = GCHandle.Alloc(dds.Blocks, GCHandleType.Pinned);
            try
            {
                for (int level = 0; level < dds.Mips.Count; level++)
                {
                    var mip = dds.Mips[level];
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, level, format,
                        mip.Width, mip.Height, 0, mip.Length, IntPtr.Add(pin.AddrOfPinnedObject(), mip.Offset));
                    CheckError("compressed upload");
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureCompressed, out int compressed);
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureCompressedImageSize, out int bytes);
                    CheckError("compressed storage query");
                    if (compressed != 1 || bytes != mip.Length)
                        throw new InvalidOperationException($"Driver did not retain DDS blocks at mip {level}: compressed={compressed}, bytes={bytes}");
                    storedBytes += bytes;
                }
            }
            finally { pin.Free(); }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, dds.Mips.Count - 1);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            if (dds.Format == DdsBlockFormat.Bc4)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleG, (int)All.Red);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)All.Red);
            }
            if (_extensions.Contains("GL_EXT_texture_filter_anisotropic") || _extensions.Contains("GL_ARB_texture_filter_anisotropic"))
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE,
                    Math.Min(GL.GetFloat((GetPName)0x84FF), 8f));
            CheckError("compressed texture parameters");
            return handle;
        }
        catch { GL.DeleteTexture(handle); throw; }
    }

    internal static void CheckError(string operation)
    {
        var error = GL.GetError();
        if (error == ErrorCode.OutOfMemory) throw new OutOfMemoryException($"OpenGL {operation}: {error}");
        if (error != ErrorCode.NoError) throw new InvalidOperationException($"OpenGL {operation}: {error}");
    }
}
