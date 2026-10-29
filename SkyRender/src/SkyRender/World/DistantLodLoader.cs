using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace SkyRender.World;

/// <summary>What a host asks for: a worldspace's distant LOD around a camera.</summary>
public sealed record DistantLodRequest
{
    /// <summary>The worldspace whose LOD files to read: its EditorID as the file names use it
    /// (e.g. <c>Tamriel</c>). A child worldspace that uses its parent's LOD passes the parent's.</summary>
    public required string Worldspace { get; init; }
    /// <summary>Camera position, Skyrim world units (Z up). Only X and Y pick the blocks.</summary>
    public required Vector3 Eye { get; init; }
    /// <summary>The cells drawn at full detail; LOD under them is cut away. Null: none.</summary>
    public LoadedCells? LoadedCells { get; init; }
    public LodKinds Kinds { get; init; } = LodKinds.All;
    public LodDistances Distances { get; init; } = new();
    /// <summary>No block farther than this is read (e.g. the host's far plane). Default: no limit.</summary>
    public float MaxDistance { get; init; } = float.PositiveInfinity;
    /// <summary>Overrides the worldspace's <c>lodsettings</c> file (null: read it).</summary>
    public LodSettings? Settings { get; init; }
}

/// <summary>The result of <see cref="DistantLodLoader.Load"/>: meshes to upload and what went into them.</summary>
public sealed class DistantLodData
{
    public LodSettings? Settings { get; init; }
    public List<LodMeshPart> Parts { get; } = new();
    /// <summary>Blocks the selector picked, per kind.</summary>
    public Dictionary<LodKind, int> BlocksSelected { get; } = new();
    /// <summary>Blocks whose file was found, per kind (a missing block draws nothing, as in the engine).
    /// For <see cref="LodKind.Water"/>: terrain blocks that hold LOD water quads.</summary>
    public Dictionary<LodKind, int> BlocksFound { get; } = new();
    /// <summary>Files that were found but could not be read, with the reason.</summary>
    public List<string> Errors { get; } = new();
    /// <summary>Why nothing could be loaded (no lodsettings file, ...), or null.</summary>
    public string? Problem { get; set; }
    public TimeSpan Elapsed { get; set; }

    public int CountOf(LodKind kind) { int n = 0; foreach (var p in Parts) if (p.Kind == kind) n++; return n; }
}

/// <summary>
/// Loads a worldspace's distant LOD for a camera, GL-free: reads the <c>lodsettings</c> file, selects
/// the blocks (<see cref="LodSelector"/>), reads each block's file and builds its meshes
/// (<see cref="LodMeshBuilder"/>). LOD water comes out of the terrain blocks' files. Files are found through the host's resolver, which maps a
/// game-relative path to a file on disk (loose or extracted from an archive), or null when no
/// active source has it; <see cref="GameAssetResolver.ResolveAssetPath"/> is one. Upload the result
/// with <see cref="GlWorldFactory.CreateDistantLod"/>.
/// </summary>
public static class DistantLodLoader
{
    public static DistantLodData Load(DistantLodRequest request, Func<string, string?> resolveDiskPath)
    {
        var sw = Stopwatch.StartNew();
        string w = request.Worldspace;
        var settings = request.Settings;
        if (settings is null)
        {
            string? lodPath = resolveDiskPath(LodSettings.PathFor(w));
            if (lodPath is null)
                return new DistantLodData { Problem = $"no {LodSettings.PathFor(w)} in the active sources", Elapsed = sw.Elapsed };
            try { settings = LodSettings.Parse(File.ReadAllBytes(lodPath)); }
            catch (Exception ex) { return new DistantLodData { Problem = $"{LodSettings.PathFor(w)}: {ex.Message}", Elapsed = sw.Elapsed }; }
        }
        var data = new DistantLodData { Settings = settings };
        var eye = new Vector2(request.Eye.X, request.Eye.Y);
        var loaded = request.LoadedCells;

        Dictionary<int, TreeLodType>? treeTypes = null;
        if ((request.Kinds & LodKinds.Trees) != 0)
        {
            string? lst = resolveDiskPath(LodPaths.TreeList(w));
            if (lst is null) data.Errors.Add($"{LodPaths.TreeList(w)}: not found; tree LOD skipped");
            else
            {
                try { treeTypes = TreeLodFiles.ParseList(File.ReadAllBytes(lst)); }
                catch (Exception ex) { data.Errors.Add($"{LodPaths.TreeList(w)}: {ex.Message}"); }
            }
        }

        bool terrain = (request.Kinds & LodKinds.Terrain) != 0, water = (request.Kinds & LodKinds.Water) != 0;
        foreach (LodKind kind in new[] { LodKind.Terrain, LodKind.Objects, LodKind.Trees })
        {
            // LOD water is part of the terrain blocks: one pass over the .btr files reads both.
            bool wanted = kind == LodKind.Terrain ? terrain || water : (request.Kinds & (LodKinds)(1 << (int)kind)) != 0;
            if (!wanted) continue;
            if (kind == LodKind.Trees && treeTypes is null) continue;
            var blocks = LodSelector.Select(kind, settings, request.Distances, eye, request.MaxDistance);
            if (kind != LodKind.Terrain || terrain) data.BlocksSelected[kind] = blocks.Count;
            if (kind == LodKind.Terrain && water) data.BlocksSelected[LodKind.Water] = blocks.Count;
            int found = 0, withWater = 0;
            foreach (var block in blocks)
            {
                string game = block.MeshPath(w);
                string? disk = resolveDiskPath(game);
                if (disk is null) continue;
                found++;
                try
                {
                    if (kind == LodKind.Trees)
                    {
                        var groups = TreeLodFiles.ParseBlock(File.ReadAllBytes(disk));
                        if (LodMeshBuilder.BuildTrees(block, groups, treeTypes!, loaded, LodPaths.TreeAtlas(w)) is { } tp)
                            data.Parts.Add(tp);
                        continue;
                    }
                    bool blockWater = false;
                    foreach (var shape in LodNifReader.Read(disk, includeWater: kind == LodKind.Terrain && water))
                    {
                        LodMeshPart? part;
                        if (shape.IsWater) { blockWater = true; part = LodMeshBuilder.BuildWater(block, shape, loaded); }
                        else if (kind == LodKind.Terrain) part = terrain ? LodMeshBuilder.BuildTerrain(block, shape, loaded) : null;
                        else part = LodMeshBuilder.BuildObjects(block, shape, loaded);
                        if (part is not null) data.Parts.Add(part);
                    }
                    if (blockWater) withWater++;
                }
                catch (Exception ex) { data.Errors.Add($"{game}: {ex.Message}"); }
            }
            if (kind != LodKind.Terrain || terrain) data.BlocksFound[kind] = found;
            if (kind == LodKind.Terrain && water) data.BlocksFound[LodKind.Water] = withWater;
        }
        data.Elapsed = sw.Elapsed;
        return data;
    }
}
