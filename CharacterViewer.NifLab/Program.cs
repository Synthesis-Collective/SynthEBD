using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using nifly;

// CharacterViewer.NifLab — CLI NIF surgery for diagnostic variant builds.
// Grown out of the 2026-07 wig head-part suppression investigation: builds
// controlled single-property variants of facegen NIFs so engine behavior can
// be bisected in-game.
//
//   NifLab api <regex>                        print NifFile method signatures matching regex
//   NifLab transplant --target T --donor D --donor-shape S=NewName [...] --strip Name [...] --out O
//   NifLab normalize --target T --shape Name [...] --part-id 131 --out O
//   NifLab setflags  --target T --shape Name=Flags [...] --out O
//
// Every command loads, mutates in memory, and saves to --out (never in place).

return args.Length == 0 ? Usage() : args[0] switch
{
    "api" => Api(args),
    "transplant" => Transplant(args),
    "normalize" => Normalize(args),
    "setflags" => SetFlags(args),
    "setroot" => SetRoot(args),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: NifLab api|transplant|normalize|setflags ... (see source header)");
    return 2;
}

// ── api: reflect over nifly to confirm SWIG signatures ──────────────────────
static int Api(string[] args)
{
    var rx = new System.Text.RegularExpressions.Regex(args.Length > 1 ? args[1] : ".", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    foreach (var t in new[] { typeof(NifFile), typeof(NiShape), typeof(NiAVObject), typeof(NiHeader) })
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (!rx.IsMatch(m.Name)) continue;
            var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"{t.Name}.{m.Name}({ps}) : {m.ReturnType.Name}");
        }
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!rx.IsMatch(p.Name)) continue;
            Console.WriteLine($"{t.Name}.{p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}} : {p.PropertyType.Name}");
        }
    }
    return 0;
}

// ── helpers ─────────────────────────────────────────────────────────────────
static Dictionary<string, List<string>> ParseOpts(string[] args)
{
    var opts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    string? key = null;
    foreach (var a in args.Skip(1))
    {
        if (a.StartsWith("--")) { key = a[2..]; if (!opts.ContainsKey(key)) opts[key] = new(); }
        else if (key != null) opts[key].Add(a);
    }
    return opts;
}

static NifFile LoadNif(string path)
{
    var nif = new NifFile();
    if (nif.Load(path) != 0) throw new InvalidOperationException($"nifly failed to load {path}");
    return nif;
}

static NiShape FindShape(NifFile nif, string name)
{
    using var shapes = nif.GetShapes();
    foreach (var s in shapes)
        if (string.Equals(s.name?.get(), name, StringComparison.OrdinalIgnoreCase)) return s;
    throw new InvalidOperationException($"shape '{name}' not found");
}

static NiNode FindNode(NifFile nif, string name)
{
    var header = nif.GetHeader();
    for (uint i = 0; i < header.GetNumBlocks(); i++)
        if (header.GetBlockById(i) is NiNode n &&
            string.Equals(n.name?.get(), name, StringComparison.OrdinalIgnoreCase)) return n;
    throw new InvalidOperationException($"node '{name}' not found");
}

static void Save(NifFile nif, string outPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    if (nif.Save(outPath) != 0) throw new InvalidOperationException($"nifly failed to save {outPath}");
    Console.WriteLine($"wrote {outPath}");
}

// ── transplant: clone donor shapes into target, strip old ones, rename ──────
static int Transplant(string[] args)
{
    var opts = ParseOpts(args);
    string targetPath = opts["target"][0];
    string donorPath = opts["donor"][0];
    string outPath = opts["out"][0];

    using var target = LoadNif(targetPath);
    using var donor = LoadNif(donorPath);

    // Strip first so name collisions with incoming clones are impossible.
    foreach (var strip in opts.TryGetValue("strip", out var st) ? st : new List<string>())
    {
        var victim = FindShape(target, strip);
        target.DeleteShape(victim);
        Console.WriteLine($"stripped '{strip}'");
    }

    var faceGenNode = FindNode(target, "BSFaceGenNiNodeSkinned");

    foreach (var spec in opts["donor-shape"])
    {
        var eq = spec.IndexOf('=');
        string srcName = eq < 0 ? spec : spec[..eq];
        string newName = eq < 0 ? spec : spec[(eq + 1)..];

        var src = FindShape(donor, srcName);
        var clone = target.CloneShape(src, newName, donor);
        if (clone == null) throw new InvalidOperationException($"CloneShape failed for '{srcName}'");
        target.SetParentNode(clone, faceGenNode);
        Console.WriteLine($"transplanted '{srcName}' -> '{newName}'");
    }

    Save(target, outPath);
    return 0;
}

// ── normalize: rebuild dismember partitions + skeleton root on named shapes ─
static int Normalize(string[] args)
{
    var opts = ParseOpts(args);
    string targetPath = opts["target"][0];
    string outPath = opts["out"][0];
    ushort partId = opts.TryGetValue("part-id", out var pid) ? ushort.Parse(pid[0]) : (ushort)131;

    using var target = LoadNif(targetPath);
    var header = target.GetHeader();
    var faceGenNode = FindNode(target, "BSFaceGenNiNodeSkinned");
    uint faceGenNodeId = header.GetBlockID(faceGenNode);

    foreach (var name in opts["shape"])
    {
        var shape = FindShape(target, name);

        using var template = new NiVectorBSDismemberSkinInstancePartitionInfo();
        using var info = new BSDismemberSkinInstance.PartitionInfo();
        info.partID = partId;
        info.flags = (PartitionFlags)0x0101;
        template.push_back(info);

        using var tris = new vectorTriangle();
        shape.GetTriangles(tris);
        using var triParts = new vectorint();
        for (int t = 0; t < tris.Count; t++) triParts.Add(0);

        target.SetShapePartitions(shape, template, triParts, true);
        target.RemoveEmptyPartitions(shape);
        target.UpdateSkinPartitions(shape);

        var skinRef = shape.SkinInstanceRef();
        if (skinRef != null && !skinRef.IsEmpty() &&
            header.GetBlockById(skinRef.index) is NiSkinInstance skinInst)
        {
            skinInst.targetRef.index = faceGenNodeId;
        }
        Console.WriteLine($"normalized '{name}' -> partition {partId}, skeleton root {faceGenNodeId}");
    }

    Save(target, outPath);
    return 0;
}

// ── setroot: repoint named shapes' skin skeleton root at a named node ───────
static int SetRoot(string[] args)
{
    var opts = ParseOpts(args);
    using var target = LoadNif(opts["target"][0]);
    var header = target.GetHeader();
    var node = FindNode(target, opts.TryGetValue("node", out var n) ? n[0] : "BSFaceGenNiNodeSkinned");
    uint nodeId = header.GetBlockID(node);

    foreach (var name in opts["shape"])
    {
        var shape = FindShape(target, name);
        var skinRef = shape.SkinInstanceRef();
        if (skinRef == null || skinRef.IsEmpty() ||
            header.GetBlockById(skinRef.index) is not NiSkinInstance skinInst)
            throw new InvalidOperationException($"'{name}' has no skin instance");
        Console.WriteLine($"'{name}' skeleton root {skinInst.targetRef.index} -> {nodeId}");
        skinInst.targetRef.index = nodeId;
    }

    Save(target, opts["out"][0]);
    return 0;
}

// ── setflags: overwrite NiAVObject flags on named shapes ────────────────────
static int SetFlags(string[] args)
{
    var opts = ParseOpts(args);
    string targetPath = opts["target"][0];
    string outPath = opts["out"][0];

    using var target = LoadNif(targetPath);
    foreach (var spec in opts["shape"])
    {
        var eq = spec.IndexOf('=');
        string name = spec[..eq];
        uint flags = Convert.ToUInt32(spec[(eq + 1)..], 10);
        var shape = FindShape(target, name);
        Console.WriteLine($"'{name}' flags {shape.flags} -> {flags}");
        shape.flags = flags;
    }

    Save(target, outPath);
    return 0;
}
