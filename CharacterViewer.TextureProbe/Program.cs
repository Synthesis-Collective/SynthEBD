using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CharacterViewer.Rendering;
using CharacterViewer.TextureProbe;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

try
{
    if (args.Length < 2) throw new ArgumentException("verify OUTPUT | bench OUTPUT TEXTURE_LIST_JSON decoded|INTEGER|unlimited | inventory OUTPUT TEXTURE_LIST_JSON");
    string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
    var log = new ProbeLog(); var gate = new CharacterViewerLogGate();
    var resolver = new GameAssetResolver(new DataRoot(output), new NoArchives(), gate, log);
    var identity = new
    {
        library = typeof(GlTextureManager).Assembly.Location,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GlTextureManager).Assembly.Location)))
    };
    if (args[0] == "inventory")
    {
        var inventory = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[2]))!.Select(path =>
        {
            var status = CompressedDds.Read(path, out var dds, out var reason, int.MaxValue);
            return new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), status = status.ToString(), reason,
                format = dds?.Format.ToString(), width = dds?.Width, height = dds?.Height, mips = dds?.Mips.Count,
                finalWidth = dds?.Mips[^1].Width, finalHeight = dds?.Mips[^1].Height, payloadBytes = dds?.PayloadBytes };
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "inventory.json"), JsonSerializer.Serialize(new { identity, inventory }, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    using var window = new GameWindow(GameWindowSettings.Default, new NativeWindowSettings
    {
        ClientSize = new Vector2i(8, 8),
        StartVisible = false,
        StartFocused = false,
        APIVersion = new Version(4, 0),
        Profile = ContextProfile.Core
    });
    Console.WriteLine(JsonSerializer.Serialize(new { identity, gpu = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version) }));
    if (args[0] == "bench") { Benchmark(); return; }
    int passed = 0;
    void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS " + description); }
    string Save(string name, byte[] bytes) { string path = Path.Combine(output, name + ".dds"); File.WriteAllBytes(path, bytes); return path; }
    CharacterPreviewCache Cache(int? maximum) => new(new NoNpcs(), resolver, log, gate) { DdsMaximumFinalMipDimension = maximum };
    using var resident = new ResidentTextureCache(log, 256L * 1024 * 1024);
    resident.BeginRenderPass();
    var compressedCache = Cache(1);
    using var manager = new GlTextureManager(compressedCache, log, resident); manager.Initialize();
    using var sampler = new Sampler();
    foreach (int format in new[] { 71, 72, 74, 75, 77, 78, 80, 83, 98, 99 })
        foreach (bool legacy in new[] { false, true })
        {
            if (legacy && format is not (71 or 74 or 77 or 80 or 83)) continue;
            foreach (var (width, height) in new[] { (1, 1), (4, 4), (7, 5) })
            {
                string path = Save($"format-{format}-{legacy}-{width}x{height}", Fixtures.Dds(format, width, height, legacy));
                Check(CompressedDds.Read(path, out var dds, out var reason) == DdsReadStatus.Eligible, $"parse {path}: {reason}");
                int handle = manager.LoadTexture(path);
                Check(handle != manager.WhiteTexture && manager.UploadDiagnostics[^1].Route == "compressed-authored", $"actual compressed upload {format}");
                for (int level = 0; level < dds!.Mips.Count; level++)
                {
                    var mip = dds.Mips[level]; byte[] sampled = sampler.Read(handle, mip.Width, mip.Height, level);
                    var expected = Fixtures.Expected(format, level);
                    Check(sampled.Select((v, i) => v == expected[i % 4]).All(x => x), $"shader samples {format}, legacy={legacy}, {width}x{height}, mip={level}");
                }
            }
        }
    Check(compressedCache.TotalDecodeCalls == 0, "ordinary eligible upload needs zero CPU decodes");
    string standard = Save("standard", Fixtures.Dds(77));
    // Channel/alpha/mip canaries change actual GL state/storage, then use the same
    // shader reader as the healthy cases. A scorer that cannot detect these is invalid.
    string canaryPath = Save("canary", Fixtures.Dds(77)); int canary = manager.LoadTexture(canaryPath);
    GL.BindTexture(TextureTarget.Texture2D, canary);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)All.Green);
    Check(!sampler.Read(canary, 4, 4, 0).Take(4).SequenceEqual(Fixtures.Expected(77, 0)), "RED wrong channel detected");
    GL.BindTexture(TextureTarget.Texture2D, canary); GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)All.Red);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)All.One);
    Check(!sampler.Read(canary, 4, 4, 0).Take(4).SequenceEqual(Fixtures.Expected(77, 0)), "RED wrong alpha detected");
    GL.BindTexture(TextureTarget.Texture2D, canary); GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleA, (int)All.Alpha);
    GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    Check(!sampler.Read(canary, 2, 2, 1).Take(4).SequenceEqual(Fixtures.Expected(77, 1)), "RED generated mip policy detected");
    // Four independently colored blocks fix orientation without image registration.
    byte[] oriented = Fixtures.Dds(71, 8, 8); byte[] colors = { 0, 248, 224, 7, 31, 0, 255, 255 };
    for (int block = 0; block < 4; block++) { oriented[148 + block * 8] = colors[block * 2]; oriented[149 + block * 8] = colors[block * 2 + 1]; }
    int orientation = manager.LoadTexture(Save("orientation", oriented)); byte[] orientPixels = sampler.Read(orientation, 8, 8, 0);
    foreach (var (index, expected) in new[] { (0, new byte[] { 255, 0, 0, 255 }), (4, new byte[] { 0, 255, 0, 255 }), (32, new byte[] { 0, 0, 255, 255 }), (36, new byte[] { 255, 255, 255, 255 }) })
        Check(orientPixels.Skip(index * 4).Take(4).SequenceEqual(expected), "block orientation " + index);
    // BC1 transparent selector alternates opaque black and transparent black. RGB
    // alone cannot distinguish this canary; independent alpha is essential.
    byte[] cutout = Fixtures.Dds(71); Array.Clear(cutout, 148, 8); cutout[150] = 255; cutout[151] = 255;
    for (int i = 152; i < 156; i++) cutout[i] = 0xCC;
    int cutoutHandle = manager.LoadTexture(Save("cutout", cutout)); byte[] cutoutPixels = sampler.Read(cutoutHandle, 4, 4, 0);
    Check(Enumerable.Range(0, 16).All(i => cutoutPixels[i * 4 + 3] == (i % 2 == 0 ? 255 : 0)), "BC1 alpha cutout selectors");
    int first = manager.LoadTexture(standard); long allocated = resident.CurrentBytes;
    manager.ClearCache();
    Check(GL.IsTexture(first), "manager clear preserves resident handle");
    Check(manager.LoadTexture(standard) == first && resident.CurrentBytes == allocated, "resident hit allocates zero bytes");
    using (var denied = new GlTextureManager(Cache(1), log) { CompressedFormatFilter = _ => false })
    {
        denied.Initialize(); int handle = denied.LoadTexture(standard);
        Check(handle != denied.WhiteTexture && denied.UploadDiagnostics[^1].Reason == "unsupported-context-or-format-filter", "unsupported capability uses decoded fallback");
    }
    foreach (int format in new[] { 81, 84, 95, 96, 97 })
    {
        var bytes = Fixtures.Dds(83); Fixtures.Put(bytes, 128, format); string path = Save("unsupported-" + format, bytes);
        Check(CompressedDds.Read(path, out _, out _) == DdsReadStatus.Fallback, $"signed/BC6H/typeless fallback {format}");
    }
    foreach (var (name, offset, value) in new[] { ("array", 140, 2), ("cube", 136, 4), ("volume", 132, 4), ("premultiplied", 144, 2) })
    {
        var bytes = Fixtures.Dds(77); Fixtures.Put(bytes, offset, value);
        Check(CompressedDds.Read(Save(name, bytes), out _, out _) == DdsReadStatus.Fallback, "layout/alpha fallback " + name);
    }
    foreach (var (name, offset, value) in new[] { ("zero-width", 16, 0), ("overflow", 16, int.MaxValue), ("mips", 28, 30), ("header", 4, 120), ("array-zero", 140, 0) })
    {
        var bytes = Fixtures.Dds(77); Fixtures.Put(bytes, offset, value);
        Check(CompressedDds.Read(Save(name, bytes), out _, out _) == DdsReadStatus.Invalid, "malformed rejected " + name);
    }
    string truncated = Save("truncated", Fixtures.Dds(77)[..^1]);
    Check(manager.LoadTexture(truncated) == manager.WhiteTexture && manager.MissingTexturePaths.Contains(truncated), "truncated mip is error and missing, not success");
    var partial = Fixtures.Dds(77); Fixtures.Put(partial, 28, 1);
    string partialPath = Save("partial", partial);
    Check(CompressedDds.Read(partialPath, out _, out var partialReason) == DdsReadStatus.Fallback && partialReason == "final-mip-exceeds-limit", "absent authored mips explicitly fall back");
    // Partial rectangular and single-level chains: exact boundary, final-level clamp,
    // and separate resident identity for policies sharing one context/cache.
    foreach (var (width, height, count, limit) in new[] { (8, 4, 2, 4), (4, 8, 2, 4), (4, 4, 1, 4), (7, 5, 2, 3) })
    {
        byte[] bytes = Fixtures.Dds(77, width, height); Fixtures.Put(bytes, 28, count);
        string path = Save($"partial-{width}-{height}-{count}", bytes);
        using var strict = new GlTextureManager(Cache(limit - 1), log, resident);
        using var permissive = new GlTextureManager(Cache(limit), log, resident);
        strict.Initialize(); permissive.Initialize();
        int decoded = strict.LoadTexture(path), compressed = permissive.LoadTexture(path);
        Check(strict.UploadDiagnostics[^1].Reason == "final-mip-exceeds-limit", "strict threshold fallback");
        Check(decoded != compressed && permissive.UploadDiagnostics[^1].Route == "compressed-authored", "threshold resident isolation");
        GL.BindTexture(TextureTarget.Texture2D, compressed);
        GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureMaxLevel, out int last);
        Check(last == count - 1, "partial chain max level");
        Check(sampler.Read(compressed, 1, 1, 20, true).SequenceEqual(Fixtures.Expected(77, count - 1)), "minification clamps to supplied last mip");
        permissive.ClearCache();
        Check(permissive.LoadTexture(path) == compressed, "partial chain resident reuse");
    }
    string alpha = Save("transparent", Fixtures.Dds(77, transparent: true));
    Check(compressedCache.IsFullyTransparent(alpha), "transparent collision proxy classification preserved");
    Check(!compressedCache.IsFullyTransparent(standard), "nonzero alpha not hidden");
    var original = compressedCache.GetOrLoadDdsPixels(standard)!.Value.Data.ToArray();
    int tinted = manager.LoadTextureWithHairTint(standard, 0.5f, 0.25f, 1f);
    Check(compressedCache.GetOrLoadDdsPixels(standard)!.Value.Data.SequenceEqual(original), "hair tint leaves shared pixels immutable");
    Check(sampler.Read(tinted, 4, 4, 0).Take(4).SequenceEqual(new byte[] { 127, 63, 255, 128 }), "hair tint pixel contract");
    int face = manager.LoadTextureWithFaceTint(standard, standard);
    Check(compressedCache.GetOrLoadDdsPixels(standard)!.Value.Data.SequenceEqual(original), "face tint leaves shared pixels immutable");
    Check(sampler.Read(face, 4, 4, 0).Take(4).SequenceEqual(new byte[] { 255, 0, 0, 128 }), "face tint pixel contract");
    byte[] singleFace = Fixtures.Dds(71, legacy: true);
    byte[] cube = new byte[128 + (singleFace.Length - 128) * 6]; Array.Copy(singleFace, cube, 128); Fixtures.Put(cube, 112, 0xFE00);
    for (int side = 0; side < 6; side++) Array.Copy(singleFace, 128, cube, 128 + side * (singleFace.Length - 128), singleFace.Length - 128);
    var envMap = manager.LoadEnvMap(Save("cubemap", cube));
    Check(envMap.IsCube && envMap.Handle != manager.WhiteTexture, "complete cubemap preserves decoded cube route");
    GL.BindTexture(TextureTarget.TextureCubeMap, envMap.Handle);
    for (int side = 0; side < 6; side++)
    {
        byte[] pixels = new byte[4 * 4 * 4]; GL.GetTexImage(TextureTarget.TextureCubeMapPositiveX + side, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Check(pixels.Take(4).SequenceEqual(Fixtures.Expected(71, 0)), "decoded cube face " + side);
    }
    byte[] uncompressed = new byte[132]; Array.Copy(singleFace, uncompressed, 128);
    Fixtures.Put(uncompressed, 12, 1); Fixtures.Put(uncompressed, 16, 1); Fixtures.Put(uncompressed, 28, 1);
    Fixtures.Put(uncompressed, 80, 0x41); Fixtures.Put(uncompressed, 84, 0); Fixtures.Put(uncompressed, 88, 32);
    Fixtures.Put(uncompressed, 92, 0xFF0000); Fixtures.Put(uncompressed, 96, 0xFF00); Fixtures.Put(uncompressed, 100, 0xFF); Fixtures.Put(uncompressed, 104, unchecked((int)0xFF000000));
    new byte[] { 30, 60, 120, 180 }.CopyTo(uncompressed, 128);
    int rgba = manager.LoadTexture(Save("rgba", uncompressed));
    Check(manager.UploadDiagnostics[^1].Reason == "uncompressed-pixel-format" && sampler.Read(rgba, 1, 1, 0).SequenceEqual(new byte[] { 120, 60, 30, 180 }), "uncompressed BGRA fallback preserves channels and alpha");
    // Same game path, different physical variants; managers are per render as in hosts.
    foreach (var (folder, transparent) in new[] { ("variant-a", false), ("variant-b", true) })
    {
        string dir = Path.Combine(output, folder, "textures"); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, "shared.dds"), Fixtures.Dds(77, transparent: transparent));
        using var scope = resolver.PushScopes(null, new[] { Path.GetDirectoryName(dir)! }, false, false);
        using var variant = new GlTextureManager(compressedCache, log, resident); variant.Initialize();
        int handle = variant.LoadTexture("textures/shared.dds");
        Check(compressedCache.IsFullyTransparent("textures/shared.dds") == transparent, "variant alpha isolation " + folder);
        Check(sampler.Read(handle, 4, 4, 0)[3] == (transparent ? 0 : 128), "variant resident texture isolation " + folder);
    }
    resident.Clear(); Check(!GL.IsTexture(first) && resident.CurrentBytes == 0, "resident clear deletes handles and resets bytes");
    manager.ClearCache(); Check(manager.LoadTexture(standard) != manager.WhiteTexture, "reload after clear");
    // Exercise the existing cache's pressure policy with tiny real handles and
    // deliberately charged logical sizes. This is an accounting test, not VRAM measurement.
    using (var pressure = new ResidentTextureCache(log, 256L * 1024 * 1024))
    {
        pressure.BeginRenderPass();
        int a = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, a);
        pressure.Add("a", a, 200L * 1024 * 1024);
        pressure.BeginRenderPass();
        int b = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, b);
        pressure.Add("b", b, 200L * 1024 * 1024);
        Check(!GL.IsTexture(a) && GL.IsTexture(b) && pressure.CurrentBytes == 200L * 1024 * 1024, "pressure evicts previous epoch and accounts bytes");
    }
    // The retained GraphicsReuseRed fixture uses the same impossible buffer request:
    // provoke a real GL refusal without consuming a card's available memory.
    using (var oomResident = new ResidentTextureCache(log, 1024L * 1024 * 1024))
    using (var oomManager = new GlTextureManager(Cache(1), log, oomResident))
    {
        oomManager.Initialize(); oomResident.BeginRenderPass();
        int binding = GL.GetInteger(GetPName.ArrayBufferBinding), buffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
        GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(1L << 40), IntPtr.Zero, BufferUsageHint.StaticDraw);
        Check(GL.GetError() == ErrorCode.OutOfMemory, "real allocation refusal available");
        GL.BufferData(BufferTarget.ArrayBuffer, new IntPtr(1L << 40), IntPtr.Zero, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ArrayBuffer, binding); GL.DeleteBuffer(buffer);
        Check(oomManager.LoadTexture(standard) == oomManager.WhiteTexture && oomResident.Count == 0 && oomResident.BudgetBytes == 512L * 1024 * 1024, "OOM refuses upload, reduces budget, caches no successful texture");
        Check(oomManager.MissingTexturePaths.Contains(standard), "OOM visible in missing diagnostics");
        Check(oomManager.LoadTexture(standard) != oomManager.WhiteTexture, "OOM result is retryable");
    }
    int originalContextHandle = manager.LoadTexture(standard);
    using (var other = new GameWindow(GameWindowSettings.Default, new NativeWindowSettings
    {
        ClientSize = new Vector2i(8, 8),
        StartVisible = false,
        StartFocused = false,
        APIVersion = new Version(4, 0),
        Profile = ContextProfile.Core
    }))
    {
        using var otherResident = new ResidentTextureCache(log, 256L * 1024 * 1024);
        using var otherManager = new GlTextureManager(Cache(1), log, otherResident);
        using var otherSampler = new Sampler(); otherResident.BeginRenderPass(); otherManager.Initialize();
        int handle = otherManager.LoadTexture(standard);
        Check(otherSampler.Read(handle, 4, 4, 0).Take(4).SequenceEqual(Fixtures.Expected(77, 0)), "separate context owns and samples its own upload");
    }
    window.Context.MakeCurrent();
    Check(GL.IsTexture(originalContextHandle) && sampler.Read(originalContextHandle, 4, 4, 0).Take(4).SequenceEqual(Fixtures.Expected(77, 0)), "second context teardown preserves first context");
    Check(GL.GetError() == ErrorCode.NoError, "no GL errors left by suite");
    File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, identity, diagnostics = manager.UploadDiagnostics }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {passed} assertions");

    void Benchmark()
    {
        string[] paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[2]))!;
        int? mode = DdsMipPolicy.Parse(args[3]);
        var cache = Cache(mode); var records = new List<object>();
        using var gpuCache = new ResidentTextureCache(log, 1024L * 1024 * 1024);
        foreach (string phase in new[] { "fresh", "warm-cpu", "resident" })
        {
            if (phase != "resident") gpuCache.Clear(); gpuCache.BeginRenderPass();
            GC.Collect(); long allocatedBefore = GC.GetTotalAllocatedBytes(); long privateBefore = Process.GetCurrentProcess().PrivateMemorySize64;
            long callsBefore = cache.TotalDecodeCalls, bytesBefore = cache.TotalDecodedBytes; double decodeBefore = cache.TotalDecodeMs;
            var timer = Stopwatch.StartNew();
            using var textures = new GlTextureManager(cache, log, gpuCache); textures.Initialize();
            foreach (string path in paths)
            {
                // Actual character diffuse consumers need classification. The manifest
                // marks diffuse files by convention; normal/specular slots stay lazy.
                if (!Path.GetFileNameWithoutExtension(path).EndsWith("_n", StringComparison.OrdinalIgnoreCase) && !Path.GetFileNameWithoutExtension(path).EndsWith("_s", StringComparison.OrdinalIgnoreCase)) cache.IsFullyTransparent(path);
                int handle = textures.LoadTexture(path); if (handle == textures.WhiteTexture) throw new Exception("Benchmark missing " + path);
            }
            double submissionMs = timer.Elapsed.TotalMilliseconds; GL.Finish(); double completedMs = timer.Elapsed.TotalMilliseconds;
            records.Add(new
            {
                phase,
                submissionMs,
                completedMs,
                logicalGpuBytes = gpuCache.CurrentBytes,
                decodeCalls = cache.TotalDecodeCalls - callsBefore,
                decodedBytes = cache.TotalDecodedBytes - bytesBefore,
                decodeMs = cache.TotalDecodeMs - decodeBefore,
                cachedDecodedBytes = cache.CachedDecodedBytes,
                managedAllocated = GC.GetTotalAllocatedBytes() - allocatedBefore,
                privateBefore,
                privateAfter = Process.GetCurrentProcess().PrivateMemorySize64,
                workingSet = Process.GetCurrentProcess().WorkingSet64,
                uploads = textures.UploadDiagnostics.ToArray()
            });
        }
        File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(new { identity, maximumFinalMipDimension = mode, records }, new JsonSerializerOptions { WriteIndented = true }));
    }

}
catch (Exception ex)
{
    Console.Error.WriteLine("PROBE FAILED: " + ex);
    Environment.ExitCode = 1;
}

sealed class DataRoot(string path) : IDataFolderProvider { public string DataFolderPath => path; public object? CurrentLoadOrderToken => null; }
sealed class NoNpcs : INpcMeshDataSource { public object? CurrentInvalidationToken => null; public ResolvedNpcMeshPaths? Resolve(NpcIdentity identity) => null; }
sealed class NoArchives : IBsaArchiveProvider
{
    public void EnsureAllArchivesOpened() { }
    public bool TryLocateInBsa(string p, out string? b) { b = null; return false; }
    public bool TryLocateInScopedBsa(string p, string f, IReadOnlyList<string> m, out string? b) { b = null; return false; }
    public bool TryExtractToDisk(string b, string p, string d, out string? e) { e = "No archive fixture"; return false; }
}
sealed class ProbeLog : ICharacterViewerLogger
{
    public void LogMessage(string m) { }
    public void LogError(string m) => Console.Error.WriteLine(m);
    public void LogError(string m, Exception e) => Console.Error.WriteLine(m + e);
}
sealed class Sampler : IDisposable
{
    readonly int program, vao, fbo, color;
    public Sampler()
    {
        int Shader(ShaderType type, string source) { int s = GL.CreateShader(type); GL.ShaderSource(s, source); GL.CompileShader(s); GL.GetShader(s, ShaderParameter.CompileStatus, out int ok); if (ok != 1) throw new Exception(GL.GetShaderInfoLog(s)); return s; }
        int vs = Shader(ShaderType.VertexShader, "#version 330 core\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.-1.,0,1);}");
        int fs = Shader(ShaderType.FragmentShader, "#version 330 core\nuniform sampler2D tex; uniform int level; uniform bool filtered; out vec4 color; void main(){color=filtered ? textureLod(tex,vec2(0.5),float(level)) : texelFetch(tex,ivec2(gl_FragCoord.xy),level);}");
        program = GL.CreateProgram(); GL.AttachShader(program, vs); GL.AttachShader(program, fs); GL.LinkProgram(program); GL.DeleteShader(vs); GL.DeleteShader(fs);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok); if (ok != 1) throw new Exception(GL.GetProgramInfoLog(program));
        vao = GL.GenVertexArray(); fbo = GL.GenFramebuffer(); color = GL.GenTexture();
    }
    public byte[] Read(int texture, int w, int h, int level, bool filtered = false)
    {
        GL.BindTexture(TextureTarget.Texture2D, color); GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, w, h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo); GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) throw new Exception("FBO incomplete");
        GL.Viewport(0, 0, w, h); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.FramebufferSrgb);
        GL.UseProgram(program); GL.Uniform1(GL.GetUniformLocation(program, "tex"), 0); GL.Uniform1(GL.GetUniformLocation(program, "level"), level); GL.Uniform1(GL.GetUniformLocation(program, "filtered"), filtered ? 1 : 0);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, texture); GL.BindVertexArray(vao); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        byte[] pixels = new byte[w * h * 4]; GL.ReadPixels(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, pixels); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); return pixels;
    }
    public void Dispose() { GL.DeleteProgram(program); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(fbo); GL.DeleteTexture(color); }
}
