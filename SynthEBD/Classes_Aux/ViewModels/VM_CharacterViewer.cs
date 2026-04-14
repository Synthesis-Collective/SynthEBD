using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Maths;
using HelixToolkit.Wpf.SharpDX;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using HelixToolkit.SharpDX;
using HxMeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
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
/// ViewModel for the 3D character viewer inline panel. Binds to UC_CharacterViewer.
/// Manages the 3D scene and loaded mesh data.
/// </summary>
public class VM_CharacterViewer : VM
{
    private readonly NifMeshBuilder _meshBuilder;
    private readonly NpcMeshResolver _npcMeshResolver;
    private readonly NifTextureLoader _textureLoader;
    private readonly BodySlideDeformer _bodySlideDeformer;
    private readonly BsdFileParser _bsdFileParser;
    private readonly GameAssetResolver _assetResolver;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;

    /// <summary>
    /// Cancels in-flight NPC loads when inputs change rapidly.
    /// </summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Tracks which mesh model corresponds to which body part for texture overrides.
    /// Keys: "Body", "Hands", "Feet", "Head".
    /// </summary>
    private readonly Dictionary<string, MeshGeometryModel3D> _modelsByBodyPart = new();

    /// <summary>
    /// Tracks first BuiltMesh per body part for MSN normal map resampling on overrides.
    /// </summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _builtMeshesByBodyPart = new();

    /// <summary>
    /// Cached built meshes (with original undeformed positions) for reapplying BodySlide without reloading.
    /// </summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    /// <summary>
    /// Cached OSD data for the current slider group, to avoid re-parsing when only weight changes.
    /// </summary>
    private List<OsdFile>? _cachedOsdFiles;

    /// <summary>
    /// Cached NPC mesh paths from the most recent LoadNpcAsync call, including TXST textures.
    /// </summary>
    private NpcMeshResolver.NpcMeshPaths? _cachedMeshPaths;

    public VM_CharacterViewer(
        NpcMeshResolver npcMeshResolver,
        NifTextureLoader textureLoader,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        GameAssetResolver assetResolver,
        IEnvironmentStateProvider environmentProvider,
        Logger logger)
    {
        _meshBuilder = new NifMeshBuilder(logger);
        _npcMeshResolver = npcMeshResolver;
        _textureLoader = textureLoader;
        _bodySlideDeformer = bodySlideDeformer;
        _bsdFileParser = bsdFileParser;
        _assetResolver = assetResolver;
        _environmentProvider = environmentProvider;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE DATA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HelixToolkit rendering pipeline — must be bound to Viewport3DX.EffectsManager.
    /// </summary>
    public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();

    /// <summary>
    /// All mesh models currently displayed in the viewport.
    /// The View observes this collection and adds/removes items from the Viewport3DX.
    /// </summary>
    public ObservableCollection<MeshGeometryModel3D> MeshModels { get; } = new();

    // ═══════════════════════════════════════════════════════════════════════
    //  VIEWER STATE
    // ═══════════════════════════════════════════════════════════════════════

    public ViewerMode Mode { get; set; } = ViewerMode.ReadOnly;

    public string StatusText { get; set; } = "No mesh loaded";

    public bool IsLoading { get; set; }

    /// <summary>
    /// NPC weight (0–100) used for BodySlide Big/Small interpolation.
    /// Read from the NPC record during LoadNpcAsync.
    /// </summary>
    public int NpcWeight { get; set; } = 50;

    /// <summary>
    /// Available texture loading strategies for the debug dropdown.
    /// </summary>
    public TextureLoadStrategy[] AvailableStrategies { get; } = Enum.GetValues<TextureLoadStrategy>();

    /// <summary>
    /// Currently selected texture loading strategy. Changing this reloads textures.
    /// </summary>
    public TextureLoadStrategy SelectedStrategy
    {
        get => _textureLoader.LoadStrategy;
        set
        {
            if (_textureLoader.LoadStrategy != value)
            {
                _textureLoader.LoadStrategy = value;
                _logger.LogMessage("CharacterViewer: Texture strategy changed to " + value);
                ReapplyAllTextures();
            }
        }
    }

    /// <summary>
    /// Viewport background color, bound to Viewport3DX.BackgroundColor.
    /// </summary>
    public System.Windows.Media.Color BackgroundColor { get; set; } = System.Windows.Media.Color.FromRgb(105, 105, 105); // DimGray

    // ═══════════════════════════════════════════════════════════════════════
    //  LIGHTING CONTROLS
    // ═══════════════════════════════════════════════════════════════════════

    private double _ambientIntensity = 20;
    /// <summary>Ambient light intensity 0–100. Default 20.</summary>
    public double AmbientIntensity
    {
        get => _ambientIntensity;
        set { _ambientIntensity = Math.Clamp(value, 0, 100); UpdateLightingColors(); }
    }

    private double _keyLightIntensity = 100;
    /// <summary>Key (main directional) light intensity 0–100. Default 100.</summary>
    public double KeyLightIntensity
    {
        get => _keyLightIntensity;
        set { _keyLightIntensity = Math.Clamp(value, 0, 100); UpdateLightingColors(); }
    }

    private double _keyLightAzimuth = -30;
    /// <summary>Key light horizontal angle in degrees. -180 to 180, 0 = front. Default -30 (slight left).</summary>
    public double KeyLightAzimuth
    {
        get => _keyLightAzimuth;
        set { _keyLightAzimuth = Math.Clamp(value, -180, 180); UpdateKeyLightDirection(); }
    }

    private double _keyLightElevation = -45;
    /// <summary>Key light vertical angle in degrees. -90 (straight down) to 90 (straight up). Default -45.</summary>
    public double KeyLightElevation
    {
        get => _keyLightElevation;
        set { _keyLightElevation = Math.Clamp(value, -90, 90); UpdateKeyLightDirection(); }
    }

    // Computed colors/directions for XAML binding
    public MediaColor AmbientLightColor { get; set; } = IntensityToGray(20);
    public MediaColor KeyLightColor { get; set; } = IntensityToGray(100);
    public MediaColor FillLightColor { get; set; } = IntensityToGray(38); // ~38% of key
    public MediaColor RimLightColor { get; set; } = IntensityToGray(25);  // ~25% of key
    public Vector3D KeyLightDirection { get; set; } = AzElToDirection(-30, -45);
    public Vector3D FillLightDirection { get; set; } = new Vector3D(0.5, 0.3, 1);
    public Vector3D RimLightDirection { get; set; } = new Vector3D(0, 0.5, -1);

    private static MediaColor IntensityToGray(double intensity)
    {
        byte v = (byte)Math.Clamp(intensity * 2.55, 0, 255);
        return MediaColor.FromRgb(v, v, v);
    }

    private static Vector3D AzElToDirection(double azimuthDeg, double elevationDeg)
    {
        double az = azimuthDeg * Math.PI / 180.0;
        double el = elevationDeg * Math.PI / 180.0;
        return new Vector3D(
            Math.Cos(el) * Math.Sin(az),
            Math.Sin(el),
            Math.Cos(el) * Math.Cos(az));
    }

    private void UpdateLightingColors()
    {
        AmbientLightColor = IntensityToGray(_ambientIntensity);
        KeyLightColor = IntensityToGray(_keyLightIntensity);
        FillLightColor = IntensityToGray(_keyLightIntensity * 0.38);
        RimLightColor = IntensityToGray(_keyLightIntensity * 0.25);
    }

    private void UpdateKeyLightDirection()
    {
        KeyLightDirection = AzElToDirection(_keyLightAzimuth, _keyLightElevation);
    }

    /// <summary>
    /// Logs current lighting settings so the user can record good defaults.
    /// </summary>
    public void LogLightingSettings()
    {
        _logger.LogMessage($"CharacterViewer: LIGHTING — Ambient={_ambientIntensity:F0}%, " +
            $"KeyLight={_keyLightIntensity:F0}%, Azimuth={_keyLightAzimuth:F0}°, Elevation={_keyLightElevation:F0}°");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE BENCHMARK
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Benchmarks all three texture loading strategies by timing ReapplyAllTextures for each.
    /// Returns results via StatusText and logger.
    /// </summary>
    public void BenchmarkTextureStrategies()
    {
        if (MeshModels.Count == 0 || _builtMeshesByBodyPart.Count == 0)
        {
            StatusText = "Load an NPC first before benchmarking";
            return;
        }

        var results = new List<(TextureLoadStrategy Strategy, long Ms)>();
        var originalStrategy = _textureLoader.LoadStrategy;

        foreach (var strategy in Enum.GetValues<TextureLoadStrategy>())
        {
            _textureLoader.LoadStrategy = strategy;

            // Warm up once
            ReapplyAllTextures();

            // Timed run
            var sw = Stopwatch.StartNew();
            const int iterations = 3;
            for (int i = 0; i < iterations; i++)
                ReapplyAllTextures();
            sw.Stop();

            long avgMs = sw.ElapsedMilliseconds / iterations;
            results.Add((strategy, avgMs));
            _logger.LogMessage($"CharacterViewer: BENCHMARK — {strategy}: {avgMs}ms avg over {iterations} iterations");
        }

        // Restore original strategy
        _textureLoader.LoadStrategy = originalStrategy;
        ReapplyAllTextures();

        var summary = string.Join(" | ", results.Select(r => $"{r.Strategy}: {r.Ms}ms"));
        StatusText = "Benchmark: " + summary;
        _logger.LogMessage("CharacterViewer: BENCHMARK RESULTS — " + summary);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Single NIF
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads a NIF file from disk and adds all its shapes to the viewport.
    /// </summary>
    public async Task LoadNifAsync(string nifPath)
    {
        IsLoading = true;
        StatusText = "Loading...";
        _logger.LogMessage("CharacterViewer: LoadNifAsync starting for '" + nifPath + "'");

        try
        {
            var meshes = await Task.Run(() => _meshBuilder.BuildFromFile(nifPath));

            Application.Current.Dispatcher.Invoke(() =>
            {
                ClearScene();
                AddMeshesToScene(meshes);
            });

            StatusText = meshes.Count > 0
                ? $"Loaded {meshes.Count} shape(s) from {Path.GetFileName(nifPath)}"
                : "No renderable shapes found";
            _logger.LogMessage("CharacterViewer: LoadNifAsync completed — " + meshes.Count + " shapes");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.LogError("CharacterViewer: Failed to load " + nifPath + ": " + ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads meshes from an already-open NifFile (e.g., from the asset pipeline).
    /// Must be called from the UI thread.
    /// </summary>
    public void LoadFromNif(nifly.NifFile nif, string displayName)
    {
        var meshes = _meshBuilder.BuildFromNif(nif);
        ClearScene();
        AddMeshesToScene(meshes);
        StatusText = meshes.Count > 0
            ? $"Loaded {meshes.Count} shape(s) from {displayName}"
            : "No renderable shapes found";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING — Full NPC
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads the full mesh set for an NPC: resolves body/hands/feet/head meshes,
    /// loads them, and applies textures. Cancels any in-flight load.
    /// </summary>
    public async Task LoadNpcAsync(FormKey npcFormKey, ILinkCache linkCache)
    {
        // Cancel any previous in-flight load
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        StatusText = "Resolving NPC meshes...";

        try
        {
            // Resolve NPC weight from record
            if (linkCache.TryResolve<Mutagen.Bethesda.Skyrim.INpcGetter>(npcFormKey, out var npcGetter))
            {
                NpcWeight = Math.Clamp((int)npcGetter.Weight, 0, 100);
            }

            // Step 1: Resolve mesh paths (off-thread)
            var meshPaths = await Task.Run(() => _npcMeshResolver.ResolveMeshPaths(npcFormKey, linkCache), cts.Token);
            if (meshPaths == null)
            {
                StatusText = "Could not resolve NPC mesh paths";
                return;
            }

            _cachedMeshPaths = meshPaths;

            cts.Token.ThrowIfCancellationRequested();

            // Step 2: Resolve asset paths and build meshes (off-thread)
            StatusText = "Loading meshes...";
            var loadResults = await Task.Run(() => LoadAllMeshParts(meshPaths), cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            // Step 3: Update scene on UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                ClearScene();
                _modelsByBodyPart.Clear();
                _builtMeshesByBodyPart.Clear();
                _cachedBodyMeshes.Clear();

                int totalShapes = 0;
                int msnShapes = 0;
                foreach (var (bodyPart, meshes) in loadResults)
                {
                    // Resolve effective texture paths: TXST overrides NIF for body parts (not Head)
                    Dictionary<int, string>? txstOverrides = null;
                    if (bodyPart != "Head" && meshPaths.TxstTextures.TryGetValue(bodyPart, out var txst))
                    {
                        txstOverrides = txst;
                    }

                    foreach (var built in meshes)
                    {
                        var model = CreateModelFromMesh(built);

                        // Determine the effective texture paths for this shape:
                        // For body/hands/feet: TXST textures override NIF BSShaderTextureSet
                        // For head: NIF paths are ground truth (FaceGen baked)
                        var effectiveTextures = new Dictionary<int, string>(built.TexturePaths);
                        if (txstOverrides != null)
                        {
                            foreach (var (slot, path) in txstOverrides)
                            {
                                effectiveTextures[slot] = path;
                            }
                        }

                        // Apply textures — with special handling for hair tint and face tint
                        if (built.IsHairTintShader && built.HairTintColor.HasValue &&
                            effectiveTextures.TryGetValue(0, out string? hairDiffuse))
                        {
                            // Hair: apply greyscale-to-palette tint on CPU
                            var (tR, tG, tB) = built.HairTintColor.Value;
                            var hairTexture = _textureLoader.LoadDdsTextureWithHairTint(hairDiffuse, tR, tG, tB);
                            if (hairTexture != null && model.Material is PhongMaterial hairMat)
                            {
                                hairMat.DiffuseMap = hairTexture;
                                hairMat.DiffuseColor = new Color4(1f, 1f, 1f, 1f);
                            }
                        }
                        else if (bodyPart == "Head" && effectiveTextures.TryGetValue(0, out string? headDiffuse) &&
                                 meshPaths.FaceTintPath != null)
                        {
                            // Head: blend face tint onto diffuse
                            var blendedTexture = _textureLoader.LoadDdsTextureWithFaceTint(
                                headDiffuse, meshPaths.FaceTintPath);
                            if (blendedTexture != null && model.Material is PhongMaterial headMat)
                            {
                                headMat.DiffuseMap = blendedTexture;
                                headMat.DiffuseColor = new Color4(1f, 1f, 1f, 1f);
                                _logger.LogMessage("CharacterViewer: Applied face tint blend to head diffuse");
                            }
                            // Apply remaining non-diffuse textures normally
                            var nonDiffuse = effectiveTextures
                                .Where(kv => kv.Key != 0)
                                .ToDictionary(kv => kv.Key, kv => kv.Value);
                            if (nonDiffuse.Count > 0)
                                _textureLoader.ApplyTexturesToModel(model, nonDiffuse);
                        }
                        else
                        {
                            // Standard texture application
                            _textureLoader.ApplyTexturesToModel(model, effectiveTextures);
                        }

                        // Apply MSN normal maps: sample texture at vertex UVs and replace vertex normals
                        if (built.IsModelSpaceNormals &&
                            effectiveTextures.TryGetValue(1, out string? normalMapPath) &&
                            model.Geometry is HxMeshGeometry3D geo)
                        {
                            var msnNormals = _textureLoader.SampleMsnNormalsAtVertices(
                                normalMapPath, built.TextureCoordinates);
                            if (msnNormals != null)
                            {
                                geo.Normals = msnNormals;
                                msnShapes++;
                            }
                        }

                        MeshModels.Add(model);

                        // Track body part mapping (first model per part)
                        if (!_modelsByBodyPart.ContainsKey(bodyPart))
                        {
                            _modelsByBodyPart[bodyPart] = model;
                        }

                        // Track built mesh per body part for override resampling
                        if (!_builtMeshesByBodyPart.ContainsKey(bodyPart))
                        {
                            _builtMeshesByBodyPart[bodyPart] = built;
                        }

                        // Cache body mesh for BodySlide reapplication
                        if (bodyPart == "Body")
                        {
                            _cachedBodyMeshes[built.ShapeName] = built;
                        }

                        totalShapes++;
                    }
                }

                string msnInfo = msnShapes > 0 ? $", {msnShapes} MSN normal map(s) applied" : "";
                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s) for NPC{msnInfo}"
                    : "No renderable shapes found for NPC";
                _logger.LogMessage("CharacterViewer: Scene loaded — " + totalShapes + " shapes" + msnInfo);
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
            if (_loadCts == cts)
            {
                IsLoading = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TEXTURE OVERRIDES
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies texture overrides from forced subgroup FilePathReplacement entries
    /// to the appropriate mesh models and texture slots. For normal map overrides
    /// on MSN shapes, resamples vertex normals from the new texture.
    /// </summary>
    public void ApplyTextureOverrides(IEnumerable<FilePathReplacement> overrides)
    {
        if (_modelsByBodyPart.Count == 0)
        {
            _logger.LogMessage("CharacterViewer: ApplyTextureOverrides called but no models loaded");
            return;
        }

        _textureLoader.ApplyTextureOverrides(_modelsByBodyPart, overrides, _builtMeshesByBodyPart);
    }

    /// <summary>
    /// Reapplies diffuse textures to all loaded models using the current texture loading strategy.
    /// Called when <see cref="SelectedStrategy"/> changes.
    /// </summary>
    private void ReapplyAllTextures()
    {
        if (MeshModels.Count == 0) return;

        _logger.LogMessage("CharacterViewer: Reapplying textures with strategy " + _textureLoader.LoadStrategy);

        // Rebuild _modelsByBodyPart if needed — reapply from cached built meshes
        foreach (var model in MeshModels)
        {
            // Find the matching built mesh by matching vertex count
            NifMeshBuilder.BuiltMesh? matchingMesh = null;
            foreach (var kvp in _builtMeshesByBodyPart)
            {
                if (_modelsByBodyPart.TryGetValue(kvp.Key, out var partModel) && partModel == model)
                {
                    matchingMesh = kvp.Value;
                    break;
                }
            }

            // Also check cached body meshes
            if (matchingMesh == null)
            {
                foreach (var kvp in _cachedBodyMeshes)
                {
                    if (model.Geometry is HxMeshGeometry3D geo &&
                        geo.Positions?.Count == kvp.Value.Positions.Count)
                    {
                        matchingMesh = kvp.Value;
                        break;
                    }
                }
            }

            if (matchingMesh != null && matchingMesh.TexturePaths.Count > 0)
            {
                _textureLoader.ApplyTexturesToModel(model, matchingMesh.TexturePaths);
            }
        }

        StatusText = "Textures reloaded (" + _textureLoader.LoadStrategy + ")";
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  BODYSLIDE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies BodySlide deformation to the body mesh using the given preset and current NPC weight.
    /// Reuses cached OSD data if available; loads from disk if the slider group has changed.
    /// </summary>
    public void ApplyBodySlide(BodySlideSetting preset, int weight)
    {
        if (_cachedBodyMeshes.Count == 0)
        {
            _logger.LogMessage("CharacterViewer: No body mesh loaded for BodySlide application");
            return;
        }

        NpcWeight = Math.Clamp(weight, 0, 100);

        // Load OSD files for this preset's slider group if not already cached
        if (preset.SliderGroup != null)
        {
            LoadOsdFilesForGroup(preset.SliderGroup);
        }

        if (_cachedOsdFiles == null || _cachedOsdFiles.Count == 0)
        {
            _logger.LogMessage("CharacterViewer: No OSD data available for slider group '" +
                (preset.SliderGroup ?? "(null)") + "'");
            return;
        }

        // Apply deformation to each cached body shape
        foreach (var kvp in _cachedBodyMeshes)
        {
            string shapeName = kvp.Key;
            var originalMesh = kvp.Value;

            // Find the corresponding model in the scene
            var model = MeshModels.FirstOrDefault(m =>
                m.Geometry is HxMeshGeometry3D geo && geo.Positions?.Count == originalMesh.Positions.Count);
            if (model?.Geometry is not HxMeshGeometry3D geometry)
            {
                continue;
            }

            // Start from a fresh copy of original positions
            var positions = new HelixToolkit.Vector3Collection(originalMesh.Positions.Count);
            foreach (var pos in originalMesh.Positions)
            {
                positions.Add(pos);
            }

            // Apply deformation
            _bodySlideDeformer.ApplyDeformation(positions, preset, NpcWeight, _cachedOsdFiles, shapeName);

            // Recalculate normals
            var normals = new HelixToolkit.Vector3Collection(originalMesh.Normals.Count);
            foreach (var n in originalMesh.Normals)
            {
                normals.Add(n);
            }
            BodySlideDeformer.RecalculateNormals(positions, originalMesh.Indices, normals);

            // Update geometry
            geometry.Positions = positions;
            geometry.Normals = normals;
            geometry.UpdateOctree();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SCENE MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    public void ClearScene()
    {
        foreach (var model in MeshModels)
            model.Dispose();
        MeshModels.Clear();
        _modelsByBodyPart.Clear();
        _builtMeshesByBodyPart.Clear();
        _cachedBodyMeshes.Clear();
        _cachedOsdFiles = null;
        _cachedMeshPaths = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private List<(string BodyPart, List<NifMeshBuilder.BuiltMesh> Meshes)> LoadAllMeshParts(
        NpcMeshResolver.NpcMeshPaths meshPaths)
    {
        var results = new List<(string, List<NifMeshBuilder.BuiltMesh>)>();

        void TryLoad(string bodyPart, string? gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return;

            string? diskPath = _assetResolver.ResolveAssetPath(gamePath);
            if (diskPath == null) return;

            var meshes = _meshBuilder.BuildFromFile(diskPath);
            if (meshes.Count > 0)
            {
                results.Add((bodyPart, meshes));
                _logger.LogMessage($"CharacterViewer: Loaded {meshes.Count} shape(s) from {bodyPart} mesh");
            }
        }

        TryLoad("Body", meshPaths.BodyMeshPath);
        TryLoad("Hands", meshPaths.HandsMeshPath);
        TryLoad("Feet", meshPaths.FeetMeshPath);
        TryLoad("Head", meshPaths.HeadMeshPath);

        return results;
    }

    private MeshGeometryModel3D CreateModelFromMesh(NifMeshBuilder.BuiltMesh built)
    {
        var geometry = new HxMeshGeometry3D
        {
            Positions = built.Positions,
            Normals = built.Normals,
            Indices = built.Indices,
            TextureCoordinates = built.TextureCoordinates,
        };

        var material = new PhongMaterial
        {
            DiffuseColor = new Color4(0.8f, 0.75f, 0.7f, 1.0f),
            SpecularColor = new Color4(0.2f, 0.2f, 0.2f, 1.0f),
            SpecularShininess = 20f,
            AmbientColor = new Color4(0.1f, 0.1f, 0.1f, 1.0f),
        };

        return new MeshGeometryModel3D
        {
            Geometry = geometry,
            Material = material,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
        };
    }

    private void AddMeshesToScene(List<NifMeshBuilder.BuiltMesh> meshes)
    {
        foreach (var built in meshes)
        {
            var model = CreateModelFromMesh(built);

            // Apply textures
            _textureLoader.ApplyTexturesToModel(model, built.TexturePaths);

            // Apply MSN normals if applicable
            if (built.IsModelSpaceNormals &&
                built.TexturePaths.TryGetValue(1, out string? normalMapPath) &&
                model.Geometry is HxMeshGeometry3D geo)
            {
                var msnNormals = _textureLoader.SampleMsnNormalsAtVertices(
                    normalMapPath, built.TextureCoordinates);
                if (msnNormals != null)
                {
                    geo.Normals = msnNormals;
                }
            }

            MeshModels.Add(model);
        }
    }

    private void LoadOsdFilesForGroup(string sliderGroup)
    {
        string dataFolder = _environmentProvider.DataFolderPath;
        string shapeDataRoot = Path.Combine(dataFolder, "CalienteTools", "BodySlide", "ShapeData");

        if (!Directory.Exists(shapeDataRoot))
        {
            _logger.LogMessage("CharacterViewer: ShapeData directory not found at '" + shapeDataRoot + "'");
            _cachedOsdFiles = new List<OsdFile>();
            return;
        }

        // Search for subdirectories whose name contains the slider group
        // (e.g., slider group "CBBE" matches "CBBE Body", "CBBE Hands", etc.)
        var matchingDirs = Directory.GetDirectories(shapeDataRoot)
            .Where(d => Path.GetFileName(d).Contains(sliderGroup, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingDirs.Length == 0)
        {
            // Fallback: scan all subdirectories
            _logger.LogMessage("CharacterViewer: No ShapeData subdirectory matching '" + sliderGroup +
                "', scanning all subdirectories");
            matchingDirs = Directory.GetDirectories(shapeDataRoot);
        }

        var allOsd = new List<OsdFile>();
        foreach (var dir in matchingDirs)
        {
            allOsd.AddRange(_bsdFileParser.ParseAllOsdInDirectory(dir));
        }

        _cachedOsdFiles = allOsd;
        _logger.LogMessage("CharacterViewer: Loaded " + allOsd.Count + " OSD file(s) for slider group '" +
            sliderGroup + "'");
    }
}
