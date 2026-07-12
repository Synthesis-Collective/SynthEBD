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
//
// BSA-resident meshes are out of scope; point the tool at a loose file or an
// extracted copy (e.g. NPC2's CharacterViewerCache).

var paths = new List<string>();
string? outDir = null;

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
    else if (args[i] is "-h" or "--help" or "/?")
    {
        Console.WriteLine("usage: NifDump [-o <outputDir>] <path.nif> [more.nif ...]");
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
