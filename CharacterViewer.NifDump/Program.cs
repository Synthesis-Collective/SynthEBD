using System;
using System.Collections.Generic;
using System.IO;
using CharacterViewer.Rendering;

// CLI wrapper around CharacterViewer.Rendering's NifSkope-style diagnostic
// dumper. Prints the full dump (header, block index, scene graph, per-block
// shader/alpha/skinning detail, referenced texture paths) for each loose .nif
// given on the command line.
//
//   NifDump <path.nif> [more.nif ...]        dump to stdout
//   NifDump -o <dir> <path.nif> [...]        write <dir>\<name>.nifdump.txt per input
//   NifDump --partitions <path.nif> [...]    one-line-per-shape skin/partition summary
//
// BSA-resident meshes are out of scope; point the tool at a loose file or an
// extracted copy (e.g. NPC2's CharacterViewerCache).

var paths = new List<string>();
string? outDir = null;
bool partitionMode = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "-o" or "--out")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("error: -o requires a directory argument");
            return 2;
        }
        outDir = args[++i];
    }
    else if (args[i] is "-p" or "--partitions")
    {
        partitionMode = true;
    }
    else if (args[i] is "-h" or "--help" or "/?")
    {
        Console.WriteLine("usage: NifDump [-o <outputDir>] [--partitions] <path.nif> [more.nif ...]");
        return 0;
    }
    else
    {
        paths.Add(args[i]);
    }
}

if (paths.Count == 0)
{
    Console.Error.WriteLine("usage: NifDump [-o <outputDir>] <path.nif> [more.nif ...]");
    return 2;
}

if (outDir != null)
    Directory.CreateDirectory(outDir);

int failures = 0;

if (partitionMode)
{
    foreach (var path in paths)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("file not found", path);
            DumpPartitions(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {path}: {ex.Message}");
            failures++;
        }
    }
    return failures == 0 ? 0 : 1;
}

foreach (var path in paths)
{
    try
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("file not found", path);

        var dump = NifDumpApi.DumpFileToString(path);

        if (outDir != null)
        {
            var outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".nifdump.txt");
            File.WriteAllText(outPath, dump);
            Console.WriteLine($"{path} -> {outPath}");
        }
        else
        {
            Console.WriteLine(dump);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"error: {path}: {ex.Message}");
        failures++;
    }
}

return failures == 0 ? 0 : 1;

// One line per shape: name, block type, skin-instance type, skeleton root,
// dismember partitions (partID/flags), plus every NiStringExtraData in the
// file (SMP physics markers etc.). Built for partition surveys across mods.
static void DumpPartitions(string path)
{
    using var nif = new nifly.NifFile();
    if (nif.Load(path) != 0)
        throw new InvalidOperationException("nifly failed to load file");

    var header = nif.GetHeader();
    Console.WriteLine($"FILE {path}");

    uint blockCount = header.GetNumBlocks();
    for (uint id = 0; id < blockCount; id++)
    {
        if (header.GetBlockById(id) is nifly.NiStringExtraData sed)
        {
            string name = sed.name?.get() ?? "";
            string value = sed.stringData?.get() ?? "";
            Console.WriteLine($"  EXTRA [{id}] \"{name}\" = \"{value}\"");
        }
    }

    using var shapes = nif.GetShapes();
    foreach (var shape in shapes)
    {
        string shapeName = shape.name?.get() ?? "(unnamed)";
        uint shapeId;
        try { shapeId = header.GetBlockID(shape); } catch { shapeId = uint.MaxValue; }
        string shapeType;
        try { shapeType = header.GetBlockTypeStringById(shapeId); } catch { shapeType = shape.GetType().Name; }

        string skinDesc = "none";
        string rootDesc = "";
        try
        {
            var skinRef = shape.SkinInstanceRef();
            if (skinRef != null && !skinRef.IsEmpty())
            {
                skinDesc = header.GetBlockTypeStringById(skinRef.index);
                if (header.GetBlockById(skinRef.index) is nifly.NiSkinInstance skinInst)
                {
                    int rootIdx = skinInst.targetRef != null ? unchecked((int)skinInst.targetRef.index) : -1;
                    string rootType = "?", rootName = "";
                    if (rootIdx >= 0)
                    {
                        try { rootType = header.GetBlockTypeStringById((uint)rootIdx); } catch { }
                        try
                        {
                            if (header.GetBlockById((uint)rootIdx) is nifly.NiAVObject rootAv)
                                rootName = rootAv.name?.get() ?? "";
                        }
                        catch { }
                    }
                    rootDesc = $" root=[{rootIdx} {rootType} \"{rootName}\"]";
                }
            }
        }
        catch { }

        string parts = "";
        try
        {
            using var partitions = new nifly.NiVectorBSDismemberSkinInstancePartitionInfo();
            using var triParts = new nifly.vectorint();
            if (nif.GetShapePartitions(shape, partitions, triParts))
            {
                using var items = partitions.items();
                var descs = new List<string>();
                for (int pi = 0; pi < items.Count; pi++)
                    descs.Add($"{(int)items[pi].partID}(0x{(ushort)items[pi].flags:X4})");
                parts = string.Join(", ", descs);
            }
        }
        catch (Exception ex) { parts = $"<error: {ex.Message}>"; }

        Console.WriteLine($"  SHAPE \"{shapeName}\"  type={shapeType}  skin={skinDesc}{rootDesc}  parts=[{parts}]");
    }
}
