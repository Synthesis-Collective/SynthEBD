using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using nifly;

namespace CharacterViewer.Rendering;

/// <summary>
/// NifSkope-style diagnostic text dump of a loaded <see cref="NifFile"/>.
///
/// Emits the file header, every block in the file, the scene-graph hierarchy,
/// and DDS metadata (size, format, mip count) for every referenced texture —
/// formatted to resemble NifSkope's block tree so a NifSkope user can
/// cross-check values quickly.
///
/// Output flows through <see cref="ICharacterViewerLogger.LogMessage"/>, the
/// same channel as the rest of the viewer's verbose logging.
///
/// Gated by both <see cref="FULL_LOGGING"/> (compile-time, developer toggle)
/// and <see cref="CharacterViewerLogGate.Verbose"/> (runtime checkbox). With
/// <c>FULL_LOGGING = false</c> the dumper emits nothing regardless of the
/// runtime flag.
/// </summary>
internal static partial class NifDiagnosticDumper
{
    /// <summary>
    /// Master toggle for the full block dump. Set to <c>true</c> while
    /// debugging shader / texture / skinning issues; flip back to <c>false</c>
    /// for normal runs. Kept as a const so the JIT can drop the dump path
    /// entirely when off.
    /// </summary>
    internal const bool FULL_LOGGING = true;

    /// <summary>
    /// Emits a full text dump of the given NIF if both <see cref="FULL_LOGGING"/>
    /// and runtime verbose logging are enabled. Catches all exceptions — a
    /// malformed NIF must never break mesh loading.
    /// </summary>
    public static void DumpIfEnabled(
        NifFile nif,
        string nifPath,
        CharacterViewerLogGate? gate,
        ICharacterViewerLogger? logger,
        GameAssetResolver? assetResolver)
    {
        // Compile-time const gate — flipping FULL_LOGGING off lets the JIT
        // strip the entire dump path. CS0162 is expected when const is true.
#pragma warning disable CS0162
        if (!FULL_LOGGING) return;
#pragma warning restore CS0162
        if (gate == null || !gate.Verbose) return;
        if (logger == null || nif == null) return;

        try
        {
            Dump(nif, nifPath, logger, assetResolver);
        }
        catch (Exception ex)
        {
            logger.LogMessage($"[NifDump] FATAL during dump: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Dump(NifFile nif, string nifPath, ICharacterViewerLogger logger, GameAssetResolver? assetResolver)
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine("════════════════════════════════════════════════════════════════════════");
        sb.AppendLine($" NIF DUMP: {nifPath}");
        sb.AppendLine("════════════════════════════════════════════════════════════════════════");

        NiHeader header;
        try { header = nif.GetHeader(); }
        catch (Exception ex)
        {
            sb.AppendLine($"  <failed to obtain NiHeader: {ex.Message}>");
            FlushFlush(logger, sb);
            return;
        }

        DumpHeader(sb, nifPath, header);
        FlushFlush(logger, sb);

        var texturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DumpBlockIndex(sb, header, logger);
        FlushFlush(logger, sb);

        DumpSceneGraph(sb, nif, header, logger);
        FlushFlush(logger, sb);

        DumpAllBlockDetails(sb, header, texturePaths, logger);
        FlushFlush(logger, sb);

        if (assetResolver != null)
        {
            DumpTextures(sb, texturePaths, assetResolver, logger);
            FlushFlush(logger, sb);
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("=== Textures ===  (no GameAssetResolver supplied — paths only)");
            foreach (var p in SortedSet(texturePaths))
                sb.AppendLine($"  {p}");
            FlushFlush(logger, sb);
        }

        sb.AppendLine("════════════════════════════════════════════════════════════════════════");
        sb.AppendLine($" END NIF DUMP: {nifPath}");
        sb.AppendLine("════════════════════════════════════════════════════════════════════════");
        FlushFlush(logger, sb);
    }

    // Keep the in-app log panel responsive: emit each section as a single
    // multi-line LogMessage rather than one call per line.
    private static void FlushFlush(ICharacterViewerLogger logger, StringBuilder sb)
    {
        if (sb.Length == 0) return;
        // Trim a single trailing newline so the log panel doesn't double-space.
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n') sb.Length--;
        if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length--;
        logger.LogMessage(sb.ToString());
        sb.Clear();
    }

    private static IEnumerable<string> SortedSet(HashSet<string> set)
    {
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Section 1 — Header
    // ════════════════════════════════════════════════════════════════════

    private static void DumpHeader(StringBuilder sb, string nifPath, NiHeader header)
    {
        sb.AppendLine();
        sb.AppendLine("=== Header ===");
        long fileBytes = -1;
        try { fileBytes = new FileInfo(nifPath).Length; } catch { }
        sb.AppendLine($"  Path:           {nifPath}");
        sb.AppendLine($"  File size:      {(fileBytes >= 0 ? fileBytes.ToString("N0") + " bytes" : "<unknown>")}");

        try
        {
            using var version = header.GetVersion();
            sb.AppendLine($"  Version string: {SafeStr(() => version.String())}");
            sb.AppendLine($"  File version:   {SafeStr(() => version.File().ToString())}");
            sb.AppendLine($"  User version:   {SafeStr(() => version.User().ToString())}");
            sb.AppendLine($"  Stream version: {SafeStr(() => version.Stream().ToString())}");
            string game = "?";
            try
            {
                if (version.IsSSE()) game = "Skyrim SE";
                else if (version.IsSK()) game = "Skyrim LE";
                else if (version.IsFO4()) game = "Fallout 4";
                else if (version.IsFO76()) game = "Fallout 76";
                else if (version.IsFO3()) game = "Fallout 3 / NV";
                else if (version.IsOB()) game = "Oblivion";
                else if (version.IsBethesda()) game = "Bethesda (other)";
            }
            catch { }
            sb.AppendLine($"  Game:           {game}");
        }
        catch (Exception ex) { sb.AppendLine($"  <version read failed: {ex.Message}>"); }

        sb.AppendLine($"  Creator:        {SafeStr(header.GetCreatorInfo)}");
        sb.AppendLine($"  Export info:    {SafeStr(header.GetExportInfo)}");
        sb.AppendLine($"  Block count:    {SafeStr(() => header.GetNumBlocks().ToString())}");
        sb.AppendLine($"  String count:   {SafeStr(() => header.GetStringCount().ToString())}");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Section 2 — Flat block index
    // ════════════════════════════════════════════════════════════════════

    private static void DumpBlockIndex(StringBuilder sb, NiHeader header, ICharacterViewerLogger logger)
    {
        sb.AppendLine();
        sb.AppendLine("=== Block Index ===");
        uint n;
        try { n = header.GetNumBlocks(); }
        catch (Exception ex) { sb.AppendLine($"  <num blocks read failed: {ex.Message}>"); return; }

        for (uint i = 0; i < n; i++)
        {
            string typeName = SafeStr(() => header.GetBlockTypeStringById(i));
            string name = "";
            try
            {
                NiObject blk = header.GetBlockById(i);
                if (blk is NiObjectNET named && named.name != null)
                {
                    string raw = SafeStr(() => named.name.get());
                    if (!string.IsNullOrEmpty(raw)) name = $"  \"{raw}\"";
                }
            }
            catch { }
            sb.Append("  [").Append(i.ToString().PadLeft(4)).Append("] ").Append(typeName).AppendLine(name);

            // Flush every 256 lines so very large NIFs don't build a 200KB string
            if (sb.Length > 32768) FlushFlush(logger, sb);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Section 3 — Scene graph
    // ════════════════════════════════════════════════════════════════════

    private static void DumpSceneGraph(StringBuilder sb, NifFile nif, NiHeader header, ICharacterViewerLogger logger)
    {
        sb.AppendLine();
        sb.AppendLine("=== Scene Graph ===");

        NiNode? root = null;
        try { root = nif.GetRootNode(); } catch { }
        if (root == null)
        {
            sb.AppendLine("  <no root node>");
            return;
        }

        var visited = new HashSet<uint>();
        DumpNodeRecursive(sb, header, root, indent: 0, visited);
    }

    private const int MaxSceneDepth = 16;

    private static void DumpNodeRecursive(StringBuilder sb, NiHeader header, NiAVObject obj, int indent, HashSet<uint> visited)
    {
        if (obj == null) return;
        if (indent > MaxSceneDepth)
        {
            sb.Append(new string(' ', indent * 2)).AppendLine("… <max depth reached>");
            return;
        }

        uint id;
        try { id = header.GetBlockID(obj); } catch { id = uint.MaxValue; }
        if (id != uint.MaxValue && !visited.Add(id))
        {
            sb.Append(new string(' ', indent * 2)).AppendLine($"… <cycle to block [{id}]>");
            return;
        }

        string typeName = id != uint.MaxValue ? SafeStr(() => header.GetBlockTypeStringById(id)) : "?";
        string name = SafeStr(() =>
        {
            if (obj is NiObjectNET nn && nn.name != null) return nn.name.get();
            return "";
        });

        var line = new StringBuilder();
        line.Append(new string(' ', indent * 2));
        line.Append("[").Append(id == uint.MaxValue ? "?" : id.ToString()).Append("] ");
        line.Append(typeName);
        if (!string.IsNullOrEmpty(name)) line.Append(" \"").Append(name).Append('"');

        try
        {
            var t = obj.transform;
            if (t != null)
            {
                var tr = t.translation;
                if (tr != null) line.AppendFormat("  T=({0:F2},{1:F2},{2:F2})", tr.x, tr.y, tr.z);
                line.AppendFormat("  S={0:F3}", t.scale);
            }
        }
        catch { }

        if (obj is NiShape shape)
        {
            try
            {
                var shaderRef = shape.ShaderPropertyRef();
                if (shaderRef != null && !shaderRef.IsEmpty()) line.Append("  shader→[").Append(shaderRef.index).Append(']');
            }
            catch { }
            try
            {
                var alphaRef = shape.AlphaPropertyRef();
                if (alphaRef != null && !alphaRef.IsEmpty()) line.Append("  alpha→[").Append(alphaRef.index).Append(']');
            }
            catch { }
            try
            {
                var skinRef = shape.SkinInstanceRef();
                if (skinRef != null && !skinRef.IsEmpty()) line.Append("  skin→[").Append(skinRef.index).Append(']');
            }
            catch { }
        }

        sb.AppendLine(line.ToString());

        if (obj is NiNode node)
        {
            NiBlockRefArrayNiAVObject? children = null;
            try { children = node.GetChildren(); } catch { }
            if (children != null)
            {
                using var refs = children.GetRefs();
                if (refs != null)
                {
                    int count = refs.Count;
                    for (int i = 0; i < count; i++)
                    {
                        NiRef? cref = null;
                        try { cref = refs[i]; } catch { }
                        if (cref == null || cref.IsEmpty()) continue;
                        NiObject? child = null;
                        try { child = header.GetBlockById(cref.index); } catch { }
                        if (child is NiAVObject av) DumpNodeRecursive(sb, header, av, indent + 1, visited);
                    }
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Section 4 — Per-block details (filled in by Phase 5 partial)
    // ════════════════════════════════════════════════════════════════════

    private static void DumpAllBlockDetails(StringBuilder sb, NiHeader header, HashSet<string> texturePaths, ICharacterViewerLogger logger)
    {
        sb.AppendLine();
        sb.AppendLine("=== Block Details ===");
        uint n;
        try { n = header.GetNumBlocks(); }
        catch (Exception ex) { sb.AppendLine($"  <num blocks read failed: {ex.Message}>"); return; }

        for (uint i = 0; i < n; i++)
        {
            try { DumpOneBlock(sb, header, i, texturePaths); }
            catch (Exception ex) { sb.AppendLine($"  <block [{i}] dump failed: {ex.Message}>"); }

            if (sb.Length > 32768) FlushFlush(logger, sb);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════

    private static string SafeStr(Func<string?> f)
    {
        try { return f() ?? ""; }
        catch { return "<err>"; }
    }
}
