using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using HelixToolkit.Maths;
using HelixToolkit.Wpf.SharpDX;
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
    private readonly Logger _logger;

    public VM_CharacterViewer(Logger logger)
    {
        _meshBuilder = new NifMeshBuilder();
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

    // ═══════════════════════════════════════════════════════════════════════
    //  LOADING
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
                ? $"Loaded {meshes.Count} shape(s) from {System.IO.Path.GetFileName(nifPath)}"
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

    public void ClearScene()
    {
        foreach (var model in MeshModels)
            model.Dispose();
        MeshModels.Clear();
    }

    private void AddMeshesToScene(List<NifMeshBuilder.BuiltMesh> meshes)
    {
        foreach (var built in meshes)
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

            var model = new MeshGeometryModel3D
            {
                Geometry = geometry,
                Material = material,
                CullMode = SharpDX.Direct3D11.CullMode.Back,
            };

            MeshModels.Add(model);
        }
    }
}
