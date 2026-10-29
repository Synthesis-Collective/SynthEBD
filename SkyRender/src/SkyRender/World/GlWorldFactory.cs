using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyRender.World;

/// <summary>
/// The GPU form of one <see cref="TerrainCell"/>: one <see cref="GlMesh"/> per quadrant. The host
/// owns it like any mesh: add <see cref="Meshes"/> to a renderer (or call
/// <see cref="GlWorldFactory.AddTo"/>), keep it across frames, dispose it on the GL thread.
/// </summary>
public sealed class GlTerrainCell : IDisposable
{
    internal GlTerrainCell(TerrainCell cell, IReadOnlyList<GlMesh> meshes)
    {
        Cell = cell;
        Meshes = meshes;
    }

    public TerrainCell Cell { get; }
    public IReadOnlyList<GlMesh> Meshes { get; }

    public void Dispose()
    {
        foreach (var m in Meshes) m.Dispose();
    }
}

/// <summary>
/// Creates terrain and water meshes from <see cref="TerrainCell"/> / <see cref="WaterSurface"/>
/// data. Requires a current GL context. Texture loading goes through the caller's delegate, so a
/// host keeps its own texture caching and accounting; it returns a GL handle, or null when the
/// texture can't be found.
/// </summary>
public static class GlWorldFactory
{
    /// <summary>A loader over a <see cref="GlTextureManager"/>: null when the path ends up in its
    /// missing-texture set.</summary>
    public static Func<string, int?> LoaderFor(GlTextureManager textures) => path =>
    {
        int handle = textures.LoadTexture(path);
        bool missing = textures.MissingTexturePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        return missing || handle == 0 ? null : handle;
    };

    /// <summary>
    /// Builds a cell's four quadrant meshes. A quadrant with no base texture, or one that can't be
    /// found, draws on <paramref name="whiteTexture"/>; alpha layers whose texture can't be found
    /// are left out, and at most <see cref="TerrainQuadrant.MaxLayers"/> are drawn.
    /// </summary>
    public static GlTerrainCell CreateTerrain(TerrainCell cell, Func<string, int?> loadTexture, int whiteTexture,
        TerrainBuildOptions? options = null)
    {
        var meshes = new List<GlMesh>(4);
        for (int q = 0; q < 4; q++)
        {
            var quad = cell.Quadrants[q];
            int baseHandle = quad?.Base is { } b && loadTexture(b.DiffusePath) is { } h ? h : whiteTexture;

            var layers = new List<TerrainLayer>();
            var layerHandles = new List<int>();
            if (quad != null)
            {
                foreach (var layer in quad.Layers)
                {
                    if (layers.Count == TerrainQuadrant.MaxLayers) break;
                    if (loadTexture(layer.Texture.DiffusePath) is not { } lh) continue;
                    layers.Add(layer);
                    layerHandles.Add(lh);
                }
            }

            var data = WorldMeshBuilder.BuildTerrainQuadrant(cell, (TerrainQuadrantIndex)q, layers, options);
            var mesh = new GlMesh();
            UploadTerrain(mesh, data);
            ApplyTerrainMaterial(mesh, baseHandle, layerHandles);
            meshes.Add(mesh);
        }
        return new GlTerrainCell(cell, meshes);
    }

    /// <inheritdoc cref="CreateTerrain(TerrainCell, Func{string, int?}, int, TerrainBuildOptions?)"/>
    public static GlTerrainCell CreateTerrain(TerrainCell cell, GlTextureManager textures, TerrainBuildOptions? options = null)
        => CreateTerrain(cell, LoaderFor(textures), textures.WhiteTexture, options);

    /// <summary>
    /// Builds a water surface mesh. Depth for the shallow-to-deep colour comes from
    /// <paramref name="ground"/> (null: everywhere deep). The noise texture, when it resolves, is
    /// the wave normal map. Opaque unless the material's shore alpha is below 1.
    /// </summary>
    public static GlMesh CreateWater(WaterSurface surface, TerrainHeightField? ground, Func<string, int?> loadTexture,
        int whiteTexture, out int submergedVertexCount)
    {
        var data = WorldMeshBuilder.BuildWater(surface, ground);
        submergedVertexCount = data.SubmergedVertexCount;
        var m = surface.Material;
        var mesh = new GlMesh();
        UploadWater(mesh, data);
        int? noise = m.NoiseTexturePath is { Length: > 0 } path ? loadTexture(path) : null;
        ApplyWaterMaterial(mesh, m, whiteTexture, noise);
        return mesh;
    }

    /// <inheritdoc cref="CreateWater(WaterSurface, TerrainHeightField?, Func{string, int?}, int, out int)"/>
    public static GlMesh CreateWater(WaterSurface surface, TerrainHeightField? ground, GlTextureManager textures)
        => CreateWater(surface, ground, LoaderFor(textures), textures.WhiteTexture, out _);

    // ---- Building blocks, for hosts that keep their own mesh cache ----

    /// <summary>Uploads terrain quadrant geometry (vertices, indices and layer weights) into
    /// <paramref name="mesh"/>, a fresh <see cref="GlMesh"/>, and sets its name and CPU positions.</summary>
    public static void UploadTerrain(GlMesh mesh, WorldMeshData data)
    {
        mesh.Upload(data.Vertices, data.Indices);
        mesh.UploadLayerWeights(data.LayerWeights ?? new float[data.Positions.Length * TerrainQuadrant.MaxLayers]);
        mesh.ShapeName = data.Name;
        mesh.CpuPositions = data.Positions;
        mesh.CpuIndices = data.Indices;
    }

    /// <summary>Sets the terrain material: the base texture and the layer textures, in the same
    /// order as the layers the mesh's weights were built from.</summary>
    public static void ApplyTerrainMaterial(GlMesh mesh, int baseTexture, IReadOnlyList<int> layerTextures,
        TerrainBlendMode blend = TerrainBlendMode.OverWithTextureAlpha)
    {
        if (layerTextures.Count > TerrainQuadrant.MaxLayers)
            throw new ArgumentException($"at most {TerrainQuadrant.MaxLayers} layers", nameof(layerTextures));
        mesh.DiffuseTexture = baseTexture;
        mesh.HasVertexColors = true;
        mesh.IsDoubleSided = true;
        mesh.IsTerrain = true;
        mesh.TerrainLayerCount = layerTextures.Count;
        mesh.TerrainBlend = blend;
        Array.Clear(mesh.TerrainLayerTextures);
        for (int i = 0; i < layerTextures.Count; i++) mesh.TerrainLayerTextures[i] = layerTextures[i];
    }

    /// <summary>Uploads water geometry into <paramref name="mesh"/>, a fresh <see cref="GlMesh"/>.</summary>
    public static void UploadWater(GlMesh mesh, WorldMeshData data)
    {
        mesh.Upload(data.Vertices, data.Indices);
        mesh.ShapeName = data.Name;
        mesh.CpuPositions = data.Positions;
        mesh.CpuIndices = data.Indices;
    }

    /// <summary>Sets the water material. <paramref name="noiseTexture"/> (the wave normal map) may be null.</summary>
    public static void ApplyWaterMaterial(GlMesh mesh, WaterMaterial material, int whiteTexture, int? noiseTexture)
    {
        mesh.DiffuseTexture = whiteTexture;   // water types have no diffuse; colour is the record's
        mesh.NormalTexture = noiseTexture ?? 0;
        mesh.HasNormalMap = noiseTexture is not null;
        mesh.HasVertexColors = true;
        bool opaque = material.ShoreAlpha >= 0.999f;
        mesh.HasAlphaBlend = !opaque;
        mesh.SrcBlendIndex = 6;   // SRC_ALPHA
        mesh.DstBlendIndex = 7;   // ONE_MINUS_SRC_ALPHA
        mesh.DepthWrite = opaque;
        mesh.IsDoubleSided = true;
        mesh.MaterialGlossiness = 80f;
        mesh.MaterialSpecularStrength = 0.5f;
        mesh.IsWater = true;
        mesh.WaterReflectionColor = material.ReflectionColor;
        mesh.WaterReflectivity = material.Reflectivity;
        mesh.WaterFresnel = material.Fresnel;
    }

    /// <summary>Adds a terrain cell's meshes to a renderer.</summary>
    public static void AddTo(this GlTerrainCell terrain, GlRenderer renderer)
    {
        foreach (var m in terrain.Meshes) renderer.AddMesh(m);
    }

    /// <summary>Removes a terrain cell's meshes from a renderer (without disposing them).</summary>
    public static void RemoveFrom(this GlTerrainCell terrain, GlRenderer renderer)
    {
        foreach (var m in terrain.Meshes) renderer.RemoveMesh(m);
    }
}
