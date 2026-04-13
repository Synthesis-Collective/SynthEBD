using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HelixToolkit.Maths;
using HelixToolkit.Wpf.SharpDX;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using HxMeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;

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
    /// Cached built meshes (with original undeformed positions) for reapplying BodySlide without reloading.
    /// </summary>
    private readonly Dictionary<string, NifMeshBuilder.BuiltMesh> _cachedBodyMeshes = new();

    /// <summary>
    /// Cached OSD data for the current slider group, to avoid re-parsing when only weight changes.
    /// </summary>
    private List<OsdFile>? _cachedOsdFiles;

    public VM_CharacterViewer(
        NpcMeshResolver npcMeshResolver,
        NifTextureLoader textureLoader,
        BodySlideDeformer bodySlideDeformer,
        BsdFileParser bsdFileParser,
        GameAssetResolver assetResolver,
        IEnvironmentStateProvider environmentProvider,
        Logger logger)
    {
        _meshBuilder = new NifMeshBuilder();
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
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _logger.LogMessage($"CharacterViewer: Failed to load {nifPath}: {ex}");
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
                _cachedBodyMeshes.Clear();

                int totalShapes = 0;
                foreach (var (bodyPart, meshes) in loadResults)
                {
                    foreach (var built in meshes)
                    {
                        var model = CreateModelFromMesh(built);

                        // Apply textures from NIF
                        _textureLoader.ApplyTexturesToModel(model, built.TexturePaths);

                        MeshModels.Add(model);

                        // Track body part mapping (first model per part)
                        if (!_modelsByBodyPart.ContainsKey(bodyPart))
                        {
                            _modelsByBodyPart[bodyPart] = model;
                        }

                        // Cache body mesh for BodySlide reapplication
                        if (bodyPart == "Body")
                        {
                            _cachedBodyMeshes[built.ShapeName] = built;
                        }

                        totalShapes++;
                    }
                }

                StatusText = totalShapes > 0
                    ? $"Loaded {totalShapes} shape(s) for NPC"
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
    /// to the appropriate mesh models and texture slots.
    /// </summary>
    public void ApplyTextureOverrides(IEnumerable<FilePathReplacement> overrides)
    {
        if (_modelsByBodyPart.Count == 0)
        {
            return;
        }

        _textureLoader.ApplyTextureOverrides(_modelsByBodyPart, overrides);
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
        _cachedBodyMeshes.Clear();
        _cachedOsdFiles = null;
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
            AmbientColor = new Color4(0.15f, 0.15f, 0.15f, 1.0f),
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
            MeshModels.Add(CreateModelFromMesh(built));
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
