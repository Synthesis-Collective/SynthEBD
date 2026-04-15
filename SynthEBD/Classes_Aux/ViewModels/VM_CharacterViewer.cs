using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using MediaColor = System.Windows.Media.Color;

namespace SynthEBD;

/// <summary>
/// Controls which editing features are available in the character viewer.
/// </summary>
[Flags]
public enum ViewerMode
{
    ReadOnly = 0,
    TextureEdit = 1,
    BodySlideEdit = 2,
    Full = TextureEdit | BodySlideEdit
}

/// <summary>
/// ViewModel for the 3D character viewer. Manages the OpenGL rendering scene,
/// loaded mesh data, textures, and user interaction.
/// </summary>
public class VM_CharacterViewer : VM
{
    private readonly NifMeshBuilder _meshBuilder;
    private readonly NpcMeshResolver _npcMeshResolver;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdFileParser;
    private readonly GameAssetResolver _assetResolver;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;

    private CancellationTokenSource? _loadCts;

    // GL rendering objects — created once, reused across NPC loads
    public GlRenderer Renderer { get; } = new();
    public GlTextureManager? TextureManager { get; private set; }
    public OrbitCamera Camera { get; } = new();

    /// <summary>Tracks which GlMesh corresponds to which body part for texture overrides.</summary>
    private readonly Dictionary<string, GlMesh> _meshesByBodyPart = new();

    /// <summary>Tracks BuiltMesh per body part for BodySlide and normal override resampling.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _builtMeshesByBodyPart = new();

    /// <summary>Cached built meshes for reapplying BodySlide without reloading.</summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    private List<OsdFile>? _cachedOsdFiles;
    private NpcMeshResolver.NpcMeshPaths? _cachedMeshPaths;

    /// <summary>Cached texture info per mesh for ReapplyAllTextures.</summary>
    private readonly Dictionary<GlMesh, TextureApplyInfo> _textureApplyInfoByMesh = new();

    private record TextureApplyInfo(
        Dictionary<int, string> EffectiveTextures,
        bool IsHairTint, float HairTintR, float HairTintG, float HairTintB,
        bool IsFaceTint, string? FaceTintPath);

    private readonly VM_Settings_General _generalSettings;

    /// <summary>True when the GL context has been initialized.</summary>
    public bool IsGlInitialized { get; private set; }

    /// <summary>Pending scene data waiting for GL context to become available.</summary>
    private (List<(string BodyPart, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadResults,
             NpcMeshResolver.NpcMeshPaths MeshPaths)? _pendingScene;

    /// <summary>Pending texture overrides to apply after scene setup.</summary>
    private List<FilePathReplacement>? _pendingTextureOverrides;

    /// <summary>Pending BodySlide to apply after scene setup.</summary>
    private (BodySlideSetting Preset, int Weight)? _pendingBodySlide;

    public VM_CharacterViewer(
        NpcMeshResolver npcMeshResolver,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        GameAssetResolver assetResolver,
        IEnvironmentStateProvider environmentProvider,
        VM_Settings_General generalSettings,
        Logger logger)
    {
        _meshBuilder = new NifMeshBuilder(logger);
        _npcMeshResolver = npcMeshResolver;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdFileParser = bsdFileParser;
        _assetResolver = assetResolver;
        _environmentProvider = environmentProvider;
        _generalSettings = generalSettings;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VIEWER STATE
    // ═══════════════════════════════════════════════════════════════════════

    public ViewerMode Mode { get; set; } = ViewerMode.ReadOnly;
    public string StatusText { get; set; } = "No mesh loaded";
    public bool IsLoading { get; set; }
    public int NpcWeight { get; set; } = 50;

    /// <summary>Viewport background color, bound to XAML.</summary>
    public MediaColor BackgroundColor { get; set; } = MediaColor.FromRgb(105, 105, 105);

    // ═══════════════════════════════════════════════════════════════════════
    //  LIGHTING CONTROLS
    // ═══════════════════════════════════════════════════════════════════════

    private double _ambientIntensity = 20;
    public double AmbientIntensity
    {
        get => _ambientIntensity;
        set { _ambientIntensity = Math.Clamp(value, 0, 100); UpdateRendererLighting(); }
    }

    private double _keyLightIntensity = 100;
    public double KeyLightIntensity
    {
        get => _keyLightIntensity;
        set { _keyLightIntensity = Math.Clamp(value, 0, 100); UpdateRendererLighting(); }
    }

    private double _keyLightAzimuth = -30;
    public double KeyLightAzimuth
    {
        get => _keyLightAzimuth;
        set { _keyLightAzimuth = Math.Clamp(value, -180, 180); UpdateRendererLighting(); }
    }

    private double _keyLightElevation = -45;
    public double KeyLightElevation
    {
        get => _keyLightElevation;
        set { _keyLightElevation = Math.Clamp(value, -90, 90); UpdateRendererLighting(); }
    }

    private void UpdateRendererLighting()
    {
        Renderer.SetAmbientIntensity((float)(_ambientIntensity / 100.0));
        Renderer.SetKeyLightIntensity((float)(_keyLightIntensity / 100.0));
        Renderer.SetKeyLightDirection((float)_keyLightAzimuth, (float)_keyLightElevation);
    }

    public void LogLightingSettings()
    {
        _logger.LogMessage($"CharacterViewer: LIGHTING — Ambient={_ambientIntensity:F0}%, " +
            $"KeyLight={_keyLightIntensity:F0}%, Azimuth={_keyLightAzimuth:F0}°, Elevation={_keyLightElevation:F0}°");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HIT TESTING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs a ray cast from screen coordinates and returns the closest hit mesh, or null.
    /// Uses Möller-Trumbore ray-triangle intersection against CPU-side geometry.
    /// </summary>
    public GlMesh? HitTest(float mouseX, float mouseY, float viewportWidth, float viewportHeight)
    {
        var (origin, direction) = Camera.ScreenPointToRay(mouseX, mouseY, viewportWidth, viewportHeight);

        GlMesh? closestMesh = null;
        float closestDist = float.MaxValue;

        foreach (var mesh in Renderer.Meshes)
        {
            if (!mesh.IsRendering) continue;
            if (mesh.CpuPositions == null || mesh.CpuIndices == null) continue;

            if (RayIntersectsMesh(origin, direction, mesh, out float dist) && dist < closestDist)
            {
                closestDist = dist;
                closestMesh = mesh;
            }
        }

        return closestMesh;
    }

    /// <summary>
    /// Möller-Trumbore ray-triangle intersection test against a mesh's CPU-side geometry.
    /// </summary>
    private static bool RayIntersectsMesh(
        OpenTK.Mathematics.Vector3 rayOrigin,
        OpenTK.Mathematics.Vector3 rayDir,
        GlMesh mesh,
        out float hitDistance)
    {
        hitDistance = float.MaxValue;
        bool anyHit = false;

        var positions = mesh.CpuPositions!;
        var indices = mesh.CpuIndices!;
        const float epsilon = 1e-6f;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var v0Sys = positions[indices[i]];
            var v1Sys = positions[indices[i + 1]];
            var v2Sys = positions[indices[i + 2]];

            // Convert System.Numerics.Vector3 to OpenTK.Mathematics.Vector3
            var v0 = new OpenTK.Mathematics.Vector3(v0Sys.X, v0Sys.Y, v0Sys.Z);
            var v1 = new OpenTK.Mathematics.Vector3(v1Sys.X, v1Sys.Y, v1Sys.Z);
            var v2 = new OpenTK.Mathematics.Vector3(v2Sys.X, v2Sys.Y, v2Sys.Z);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = OpenTK.Mathematics.Vector3.Cross(rayDir, edge2);
            float a = OpenTK.Mathematics.Vector3.Dot(edge1, h);

            if (a > -epsilon && a < epsilon) continue; // parallel

            float f = 1f / a;
            var s = rayOrigin - v0;
            float u = f * OpenTK.Mathematics.Vector3.Dot(s, h);
            if (u < 0f || u > 1f) continue;

            var q = OpenTK.Mathematics.Vector3.Cross(s, edge1);
            float v = f * OpenTK.Mathematics.Vector3.Dot(rayDir, q);
            if (v < 0f || u + v > 1f) continue;

            float t = f * OpenTK.Mathematics.Vector3.Dot(edge2, q);
            if (t > epsilon && t < hitDistance)
            {
                hitDistance = t;
                anyHit = true;
            }
        }

        return anyHit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  GL INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called once after the GL context is ready (from the view's OnRender or Loaded event).
    /// </summary>
    public void InitializeGl(string shaderDirectory)
    {
        if (IsGlInitialized) return;

        TextureManager = new GlTextureManager(_assetResolver, _logger);
        TextureManager.Initialize();
        Renderer.Initialize(shaderDirectory);
        UpdateRendererLighting();
        IsGlInitialized = true;

        _logger.LogMessage("CharacterViewer: GL initialized");
    }

    /// <summary>
    /// Called from the GL render callback to process any pending scene setup.
    /// All GL calls (mesh upload, texture loading) happen here where the
    /// GL context is guaranteed to be current.
    /// </summary>
    public void ProcessPendingScene()
    {
        if (_pendingScene == null || !IsGlInitialized) return;

        var (loadResults, meshPaths) = _pendingScene.Value;
        _pendingScene = null;

        ClearScene();
        _cachedMeshPaths = meshPaths;

        int totalShapes = 0;
        foreach (var (bodyPart, meshes) in loadResults)
        {
            Dictionary<int, string>? txstOverrides = null;
            if (bodyPart != "Head" && meshPaths.TxstTextures.TryGetValue(bodyPart, out var txst))
                txstOverrides = txst;

            foreach (var built in meshes)
            {
                var glMesh = CreateGlMesh(built);

                var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
                if (txstOverrides != null)
                    foreach (var (slot, path) in txstOverrides)
                        effectiveTextures[slot] = path;

                bool isHairTint = false;
                float hairR = 0, hairG = 0, hairB = 0;
                bool isFaceTint = false;
                string? faceTintPath = null;

                ApplyTexturesToGlMesh(glMesh, built, effectiveTextures, meshPaths,
                    ref isHairTint, ref hairR, ref hairG, ref hairB,
                    ref isFaceTint, ref faceTintPath);

                _textureApplyInfoByMesh[glMesh] = new TextureApplyInfo(
                    new Dictionary<int, string>(effectiveTextures),
                    isHairTint, hairR, hairG, hairB, isFaceTint, faceTintPath);

                glMesh.BodyPart = bodyPart;
                Renderer.AddMesh(glMesh);

                if (bodyPart == "Head")
                {
                    if (built.IsPrimaryHeadShape || !_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (built.IsPrimaryHeadShape || !_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }
                else
                {
                    if (!_meshesByBodyPart.ContainsKey(bodyPart))
                        _meshesByBodyPart[bodyPart] = glMesh;
                    if (!_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        _builtMeshesByBodyPart[bodyPart] = built;
                }

                if (bodyPart == "Body")
                    _cachedBodyMeshes[built.ShapeName] = built;

                totalShapes++;
            }
        }

        StatusText = totalShapes > 0
            ? $"Loaded {totalShapes} shape(s) for NPC"
            : "No renderable shapes found for NPC";
        IsLoading = false;

        _logger.LogMessage($"CharacterViewer: Scene setup complete — {totalShapes} shapes, " +
            $"{Renderer.Meshes.Count} GL meshes");

        // Process any pending overrides that were queued before the scene was ready
        if (_pendingTextureOverrides != null)
        {
            var overrides = _pendingTextureOverrides;
            _pendingTextureOverrides = null;
            ApplyTextureOverrides(overrides);
        }

        if (_pendingBodySlide != null)
        {
            var (preset, weight) = _pendingBodySlide.Value;
            _pendingBodySlide = null;
            ApplyBodySlide(preset, weight);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    public async Task LoadNpcAsync(FormKey npcFormKey, ILinkCache linkCache)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        StatusText = "Resolving NPC meshes...";

        try
        {
            if (linkCache.TryResolve<Mutagen.Bethesda.Skyrim.INpcGetter>(npcFormKey, out var npcGetter))
                NpcWeight = Math.Clamp((int)npcGetter.Weight, 0, 100);

            var meshPaths = await Task.Run(() => _npcMeshResolver.ResolveMeshPaths(npcFormKey, linkCache), cts.Token);
            if (meshPaths == null)
            {
                StatusText = "Could not resolve NPC mesh paths";
                return;
            }

            _cachedMeshPaths = meshPaths;
            cts.Token.ThrowIfCancellationRequested();

            StatusText = "Loading meshes...";
            var loadResults = await Task.Run(() => LoadAllMeshParts(meshPaths), cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            // Store pending scene data — GL work is deferred to the render callback
            // where the GL context is guaranteed to be current.
            int totalShapes = loadResults.Sum(r => r.Meshes.Count);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _pendingScene = (loadResults, meshPaths);
                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s), setting up scene..."
                    : "No renderable shapes found for NPC";
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogMessage("CharacterViewer: NPC load cancelled");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.LogError($"CharacterViewer: Failed to load NPC {npcFormKey}: {ex.Message}");
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE APPLICATION
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplyTexturesToGlMesh(GlMesh glMesh, NifMeshBuilder.BuiltMesh built,
        Dictionary<int, string> effectiveTextures, NpcMeshResolver.NpcMeshPaths meshPaths,
        ref bool isHairTint, ref float hairR, ref float hairG, ref float hairB,
        ref bool isFaceTint, ref string? faceTintPath)
    {
        if (TextureManager == null) return;

        // Diffuse (slot 0) — with special handling for hair tint and face tint
        if (built.IsHairTintShader && built.HairTintColor.HasValue &&
            effectiveTextures.TryGetValue(0, out string? hairDiffuse))
        {
            var (tR, tG, tB) = built.HairTintColor.Value;
            isHairTint = true; hairR = tR; hairG = tG; hairB = tB;
            // Upload the raw greyscale texture — the shader handles tinting via
            // baseColor.rrr * tint_color * greyscaleToPaletteScale.
            // CPU-side tinting was double-applying the color (once on CPU, once in shader).
            glMesh.DiffuseTexture = TextureManager.LoadTexture(hairDiffuse);
            glMesh.HasGreyscaleToPalette = true;
            glMesh.TintColor = new System.Numerics.Vector3(tR, tG, tB);
            glMesh.GreyscaleToPaletteScale = built.GreyscaleToPaletteScale;
        }
        else if (built.IsPrimaryHeadShape && effectiveTextures.TryGetValue(0, out string? headDiffuse) &&
                 meshPaths.FaceTintPath != null)
        {
            isFaceTint = true;
            faceTintPath = meshPaths.FaceTintPath;
            glMesh.DiffuseTexture = TextureManager.LoadTextureWithFaceTint(headDiffuse, meshPaths.FaceTintPath);
        }
        else if (effectiveTextures.TryGetValue(0, out string? diffusePath))
        {
            glMesh.DiffuseTexture = TextureManager.LoadTexture(diffusePath);
        }
        else
        {
            glMesh.DiffuseTexture = TextureManager.WhiteTexture;
        }

        // Normal map (slot 1)
        if (effectiveTextures.TryGetValue(1, out string? normalPath))
        {
            glMesh.NormalTexture = TextureManager.LoadTexture(normalPath);
            glMesh.HasNormalMap = true;
            glMesh.IsModelSpace = built.IsModelSpaceNormals;
        }
        else
        {
            glMesh.NormalTexture = TextureManager.WhiteTexture;
        }

        // Skin/subsurface map (slot 2) — only meaningful for skin/face shader types.
        // For other shader types (eye, hair, default, etc.) slot 2 has a different meaning
        // (glow, environment, etc.) and applying it as a skin map would add incorrect red SSS tinting.
        bool isSkinShader = built.ShaderType == 4  // BSLSP_FACE
                         || built.ShaderType == 5; // BSLSP_SKINTINT
        if (isSkinShader && effectiveTextures.TryGetValue(2, out string? skinPath))
        {
            glMesh.SkinTexture = TextureManager.LoadTexture(skinPath);
            glMesh.HasSkinMap = true;
        }
        else
        {
            glMesh.SkinTexture = TextureManager.WhiteTexture;
        }

        // Specular map (slot 7)
        if (effectiveTextures.TryGetValue(7, out string? specPath))
        {
            glMesh.SpecularTexture = TextureManager.LoadTexture(specPath);
            glMesh.HasSpecularMap = true;
            glMesh.HasSpecular = true;
        }
        else
        {
            glMesh.SpecularTexture = TextureManager.WhiteTexture;
            // Check shader flags for specular enable even without a map
            glMesh.HasSpecular = (built.ShaderFlags1 & (1u << 0)) != 0;
        }

        glMesh.FaceTintTexture = TextureManager.WhiteTexture;

        // Material properties
        glMesh.MaterialGlossiness = built.Glossiness;
        glMesh.MaterialSpecularStrength = built.SpecularStrength;
        glMesh.SpecularColor = built.SpecularColor;
        glMesh.SubsurfaceRolloff = built.SubsurfaceRolloff;
        glMesh.RimlightPower = built.RimlightPower;
        glMesh.HasVertexColors = built.HasVertexColors;
        glMesh.UvScale = built.UvScale;
        glMesh.UvOffset = built.UvOffset;

        // Emissive (SLSF1_OwnEmit, bit 22)
        if ((built.ShaderFlags1 & (1u << 22)) != 0)
        {
            glMesh.HasEmissive = true;
            glMesh.EmissiveColor = built.EmissiveColor;
            glMesh.EmissiveMultiple = built.EmissiveMultiple;
        }

        // Shader type flags (bit positions per Nifskope/Bethesda spec)
        glMesh.HasHairSoftLighting = (built.ShaderFlags1 & (1u << 18)) != 0; // SLSF1_Hair_Soft_Lighting
        glMesh.HasSoftLighting = (built.ShaderFlags2 & (1u << 25)) != 0; // SLSF2_Soft_Lighting
        glMesh.HasRimLighting = (built.ShaderFlags2 & (1u << 26)) != 0; // SLSF2_Rim_Lighting

        // Eye shader (shader type 16 = ST_EyeEnvmap)
        if (built.ShaderType == 16)
        {
            glMesh.IsEye = true;
        }

        // Environment mapping (SLSF1_Environment_Mapping bit 7, or SLSF1_Eye_Environment_Mapping bit 17)
        bool hasEnvMap = (built.ShaderFlags1 & (1u << 7)) != 0;
        bool hasEyeEnvMap = (built.ShaderFlags1 & (1u << 17)) != 0;
        if ((hasEnvMap || hasEyeEnvMap) && effectiveTextures.TryGetValue(4, out string? envMapPath))
        {
            var envTex = TextureManager.LoadCubemap(envMapPath);
            if (envTex != 0)
            {
                glMesh.EnvMapTexture = envTex;
                glMesh.HasEnvironmentMap = true;
                glMesh.EnvMapScale = built.EnvironmentMapScale;
                glMesh.EyeCubemapScale = built.EyeCubemapScale;
            }

            if (effectiveTextures.TryGetValue(5, out string? envMaskPath))
            {
                glMesh.EnvMaskTexture = TextureManager.LoadTexture(envMaskPath);
                glMesh.HasEnvMask = true;
            }
        }

        // Detail map (SLSF1_Facegen_Detail_Map, bit 10)
        if ((built.ShaderFlags1 & (1u << 10)) != 0 && effectiveTextures.TryGetValue(3, out string? detailPath))
        {
            glMesh.DetailTexture = TextureManager.LoadTexture(detailPath);
            glMesh.HasDetailMap = true;
        }

        // Double-sided (brow, eyelash, hair — thin geometry visible from both sides)
        glMesh.IsDoubleSided = built.IsDoubleSided;

        // Alpha test / blend
        if (built.HasAlphaTest || built.HasAlphaBlend)
        {
            if (glMesh.DiffuseTexture != TextureManager.WhiteTexture)
            {
                glMesh.UseAlphaTest = built.HasAlphaTest;
                glMesh.HasAlphaBlend = built.HasAlphaBlend;
                glMesh.AlphaThreshold = built.AlphaThreshold;
            }
            else
            {
                glMesh.IsRendering = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyTextureOverrides(IEnumerable<FilePathReplacement> overrides)
    {
        var overrideList = overrides.ToList();

        // If scene isn't set up yet (pending GL work), queue for later
        if (_meshesByBodyPart.Count == 0 || TextureManager == null)
        {
            _pendingTextureOverrides = overrideList;
            return;
        }

        foreach (var replacement in overrides)
        {
            string dest = replacement.Destination;
            if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(replacement.Source))
                continue;

            string? bodyPart = ParseBodyPart(dest);
            int? slot = ParseTextureSlot(dest);
            if (bodyPart == null || slot == null) continue;
            if (!_meshesByBodyPart.TryGetValue(bodyPart, out var mesh)) continue;

            if (slot.Value == 0)
            {
                // Diffuse override
                if (bodyPart == "Head" && _cachedMeshPaths?.FaceTintPath != null)
                    mesh.DiffuseTexture = TextureManager.LoadTextureWithFaceTint(
                        replacement.Source, _cachedMeshPaths.FaceTintPath);
                else
                    mesh.DiffuseTexture = TextureManager.LoadTexture(replacement.Source);
            }
            else if (slot.Value == 1)
            {
                // Normal map override — the shader handles MSN natively, no CPU resampling needed!
                mesh.NormalTexture = TextureManager.LoadTexture(replacement.Source);
                mesh.HasNormalMap = true;
                _logger.LogMessage("CharacterViewer: Normal map override '" + replacement.Source + "' → " + bodyPart);
            }
            else if (slot.Value == 7)
            {
                mesh.SpecularTexture = TextureManager.LoadTexture(replacement.Source);
                mesh.HasSpecularMap = true;
                mesh.HasSpecular = true;
            }
            else
            {
                _logger.LogMessage("CharacterViewer: Skipping override for slot " + slot.Value);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYSLIDE
    // ═══════════════════════════════════════════════════════════════════════

    public void ApplyBodySlide(BodySlideSetting preset, int weight)
    {
        // If scene isn't set up yet (pending GL work), queue for later
        if (_cachedBodyMeshes.Count == 0)
        {
            _pendingBodySlide = (preset, weight);
            return;
        }

        NpcWeight = Math.Clamp(weight, 0, 100);

        if (preset.SliderGroup != null)
            LoadOsdFilesForGroup(preset.SliderGroup);

        if (_cachedOsdFiles == null || _cachedOsdFiles.Count == 0) return;

        foreach (var kvp in _cachedBodyMeshes)
        {
            string shapeName = kvp.Key;
            var originalMesh = kvp.Value;

            // Find the GL mesh for this shape
            var glMesh = Renderer.Meshes.FirstOrDefault(m => m.ShapeName == shapeName);
            if (glMesh == null) continue;

            // Start from bind-pose positions
            var sourcePositions = originalMesh.BindPosePositions ?? originalMesh.Positions;
            var positions = new Vector3[sourcePositions.Length];
            Array.Copy(sourcePositions, positions, sourcePositions.Length);

            // Apply deformation
            _bodySlideDeformer.ApplyDeformation(positions, preset, NpcWeight, _cachedOsdFiles, shapeName);

            // Recalculate normals
            var sourceNormals = originalMesh.BindPoseNormals ?? originalMesh.Normals;
            var normals = new Vector3[sourceNormals.Length];
            Array.Copy(sourceNormals, normals, sourceNormals.Length);
            BodySlideDeformer.RecalculateNormals(positions, originalMesh.Indices, normals);

            // Re-apply skinning
            if (originalMesh.Skinning != null)
                NifMeshBuilder.ApplySkinning(positions, normals, originalMesh.Skinning, positions, normals);

            // Re-upload vertex data to GPU
            var vertexData = BuildInterleavedVertexData(positions, normals,
                originalMesh.TextureCoordinates, originalMesh.Tangents, originalMesh.Bitangents,
                originalMesh.VertexColors);
            glMesh.UpdateVertexData(vertexData);

            // Update CPU-side positions for hit testing
            glMesh.CpuPositions = positions;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    public void ClearScene()
    {
        Renderer.ClearMeshes();
        _meshesByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _textureApplyInfoByMesh.Clear();
        _cachedOsdFiles = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private List<(string BodyPart, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        NpcMeshResolver.NpcMeshPaths meshPaths)
    {
        var results = new List<(string, List<NifMeshBuilder.BuiltMesh>)>();

        nifly.NifFile? skeletonNif = null;
        if (!string.IsNullOrWhiteSpace(meshPaths.SkeletonPath))
        {
            string? skelDiskPath = _assetResolver.ResolveAssetPath(meshPaths.SkeletonPath);
            if (skelDiskPath != null)
            {
                skeletonNif = new nifly.NifFile();
                if (skeletonNif.Load(skelDiskPath) != 0)
                {
                    skeletonNif.Dispose();
                    skeletonNif = null;
                }
            }
        }

        try
        {
            void TryLoad(string bodyPart, string? gamePath)
            {
                if (string.IsNullOrWhiteSpace(gamePath)) return;
                string? diskPath = _assetResolver.ResolveAssetPath(gamePath);
                if (diskPath == null) return;
                var meshes = _meshBuilder.BuildFromFile(diskPath, skeletonNif);
                if (meshes.Count > 0)
                    results.Add((bodyPart, meshes));
            }

            TryLoad("Body", meshPaths.BodyMeshPath);
            TryLoad("Hands", meshPaths.HandsMeshPath);
            TryLoad("Feet", meshPaths.FeetMeshPath);
            TryLoad("Head", meshPaths.HeadMeshPath);
        }
        finally
        {
            skeletonNif?.Dispose();
        }

        return results;
    }

    /// <summary>
    /// Creates a GlMesh from a BuiltMesh, uploading interleaved vertex data and indices.
    /// </summary>
    private GlMesh CreateGlMesh(NifMeshBuilder.BuiltMesh built)
    {
        var vertexData = BuildInterleavedVertexData(
            built.Positions, built.Normals, built.TextureCoordinates,
            built.Tangents, built.Bitangents, built.VertexColors);

        var glMesh = new GlMesh();
        glMesh.Upload(vertexData, built.Indices);
        glMesh.ShapeName = built.ShapeName;
        glMesh.IsPrimaryHeadShape = built.IsPrimaryHeadShape;

        // Store CPU-side geometry for ray-based hit testing
        glMesh.CpuPositions = (Vector3[])built.Positions.Clone();
        glMesh.CpuIndices = (int[])built.Indices.Clone();

        return glMesh;
    }

    /// <summary>
    /// Builds interleaved vertex data array for the GL mesh.
    /// Layout per vertex: position(3) + normal(3) + texcoord(2) + color(4) + tangent(3) + bitangent(3) = 18 floats
    /// </summary>
    private static float[] BuildInterleavedVertexData(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        Vector3[] tangents, Vector3[] bitangents, Vector4[]? vertexColors = null)
    {
        int vertCount = positions.Length;
        var data = new float[vertCount * 18];

        for (int i = 0; i < vertCount; i++)
        {
            int offset = i * 18;
            var p = positions[i];
            var n = i < normals.Length ? normals[i] : Vector3.UnitY;
            var uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            var t = i < tangents.Length ? tangents[i] : Vector3.Zero;
            var b = i < bitangents.Length ? bitangents[i] : Vector3.Zero;

            // Position
            data[offset]     = p.X;
            data[offset + 1] = p.Y;
            data[offset + 2] = p.Z;
            // Normal
            data[offset + 3] = n.X;
            data[offset + 4] = n.Y;
            data[offset + 5] = n.Z;
            // UV
            data[offset + 6] = uv.X;
            data[offset + 7] = uv.Y;
            // Vertex color
            if (vertexColors != null && i < vertexColors.Length)
            {
                var vc = vertexColors[i];
                data[offset + 8]  = vc.X;
                data[offset + 9]  = vc.Y;
                data[offset + 10] = vc.Z;
                data[offset + 11] = vc.W;
            }
            else
            {
                data[offset + 8]  = 1f;
                data[offset + 9]  = 1f;
                data[offset + 10] = 1f;
                data[offset + 11] = 1f;
            }
            // Tangent
            data[offset + 12] = t.X;
            data[offset + 13] = t.Y;
            data[offset + 14] = t.Z;
            // Bitangent
            data[offset + 15] = b.X;
            data[offset + 16] = b.Y;
            data[offset + 17] = b.Z;
        }

        return data;
    }

    private void LoadOsdFilesForGroup(string sliderGroup)
    {
        string dataFolder = _environmentProvider.DataFolderPath;
        string shapeDataRoot = Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");
        if (!Directory.Exists(shapeDataRoot))
        {
            _cachedOsdFiles = new List<OsdFile>();
            return;
        }

        var matchingDirs = Directory.GetDirectories(shapeDataRoot)
            .Where(d => Path.GetFileName(d).Contains(sliderGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingDirs.Length == 0)
            matchingDirs = Directory.GetDirectories(shapeDataRoot);

        var allOsd = new List<OsdFile>();
        foreach (var dir in matchingDirs)
            allOsd.AddRange(_bsdFileParser.ParseAllOsdInDirectory(dir));

        _cachedOsdFiles = allOsd;
    }

    private static string? ParseBodyPart(string destination)
    {
        if (destination.StartsWith("HeadTexture", StringComparison.OrdinalIgnoreCase))
            return "Head";
        if (destination.Contains("SkinTexture", StringComparison.OrdinalIgnoreCase) ||
            destination.Contains("WorldModel", StringComparison.OrdinalIgnoreCase))
        {
            if (destination.Contains("BipedObjectFlag.Body", StringComparison.OrdinalIgnoreCase)) return "Body";
            if (destination.Contains("BipedObjectFlag.Hands", StringComparison.OrdinalIgnoreCase)) return "Hands";
            if (destination.Contains("BipedObjectFlag.Feet", StringComparison.OrdinalIgnoreCase)) return "Feet";
        }
        return null;
    }

    private static int? ParseTextureSlot(string destination)
    {
        if (destination.Contains("BacklightMaskOrSpecular", StringComparison.OrdinalIgnoreCase)) return 7;
        if (destination.Contains("NormalOrGloss", StringComparison.OrdinalIgnoreCase)) return 1;
        if (destination.Contains("GlowOrDetailMap", StringComparison.OrdinalIgnoreCase)) return 2;
        if (destination.Contains("Diffuse", StringComparison.OrdinalIgnoreCase)) return 0;
        if (destination.Contains("Height", StringComparison.OrdinalIgnoreCase)) return 3;
        return null;
    }
}
