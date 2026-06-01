using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SynthEBD;

/// <summary>
/// Disk-backed measurement cache for <see cref="VM_BodyTypeProfile"/>. One JSON file per
/// profile (<c>&lt;ProfileId&gt;.measurement_cache.json</c>) holds one or more
/// <see cref="MeshSnapshot"/>s keyed by human-readable ShapeName (e.g. "CBBE 3BA",
/// "BHUNP"). Each snapshot captures the (BodyMeshHash, MeasurementFingerprints, Entries)
/// tuple required to re-use scan output across sessions without rescanning.
///
/// <para>What's cached: just the per-(preset, gender, weight) measurement values. Rule
/// evaluation re-runs from cached measurements each session, so rule edits never
/// invalidate cache entries. The expensive part — mesh deformation + per-vertex distance
/// computation — is the only thing we persist.</para>
///
/// <para>Invalidation is per-entry and per-measurement:
/// <list type="bullet">
///   <item><description>A new preset (no cached entry) scans normally and gets appended.</description></item>
///   <item><description>A modified preset (slider values changed) shows up as a
///   <see cref="CachedEntry.PresetSliderHash"/> mismatch and gets rescanned end-to-end.</description></item>
///   <item><description>A new measurement definition added to the profile: only the new
///   measurement runs across all entries; existing measurements stay cached.</description></item>
///   <item><description>An edited measurement definition or one of its dependent key
///   vertices: that single measurement's <see cref="MeshSnapshot.MeasurementFingerprints"/>
///   entry changes, and per-entry <see cref="CachedMeasurement.Fp"/> mismatches drop it
///   for recomputation. Other measurements in the same entry survive.</description></item>
///   <item><description>A different body mesh (e.g. user briefly swaps to BHUNP):
///   different ShapeName → side-by-side snapshot, original snapshot untouched.</description></item>
///   <item><description>Same ShapeName but a body-mod update: <see cref="MeshSnapshot.BodyMeshHash"/>
///   mismatch → snapshot entries are cleared and rescanned; the snapshot key stays.</description></item>
/// </list></para>
///
/// <para>Format is plain JSON via the shared <see cref="JSONhandler{T}"/>. For a 25 k-entry
/// 3BA cache this lands around 10-15 MB and loads in ~1 s. If that becomes a bottleneck,
/// the obvious refinement is binary serialization (MessagePack/BSON) without changing the
/// in-memory shape.</para>
/// </summary>
public class MeasurementCacheData
{
    /// <summary>Schema version. Bump when the layout changes incompatibly so loaders can
    /// either migrate or discard. Mismatched versions discard the whole file (safer than a
    /// partial migration; the user just pays one extra rescan).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Owning <see cref="BodyTypeProfile.Id"/>. Saved for sanity checks against
    /// the on-disk filename; mismatches log a warning but don't block load.</summary>
    public string ProfileId { get; set; } = "";

    /// <summary>Snapshots keyed by human-readable ShapeName. Each entry is independently
    /// reusable: a session running against "CBBE 3BA" reads only that snapshot; a session
    /// against "BHUNP" reads only that one. New ShapeNames append; existing ones are
    /// validated against <see cref="MeshSnapshot.BodyMeshHash"/> on load.</summary>
    public Dictionary<string, MeshSnapshot> MeshSnapshots { get; set; }
        = new(StringComparer.Ordinal);
}

/// <summary>One body-mesh-specific cache. <see cref="BodyMeshHash"/> distinguishes
/// updates within the same ShapeName (mod update, build conversion); on mismatch the
/// snapshot's entries are cleared and rescanned but the snapshot key stays.</summary>
public class MeshSnapshot
{
    /// <summary>Hash of the current body mesh's topology (vertex counts per shape). Cheap
    /// to compute from <c>viewer.GetCurrentShapeVertexCounts()</c>; doesn't require
    /// reading the .nif/.tri files themselves. Coarse enough to ride out morph-target
    /// edits that don't change topology (intentional — those typically don't change
    /// measurements meaningfully either), strict enough to catch real mesh swaps.</summary>
    public string BodyMeshHash { get; set; } = "";

    /// <summary>Wall-clock time the snapshot last fed a session. Used by the optional
    /// snapshot-purge UI to surface dormant snapshots from old body-mod experiments.</summary>
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Per-measurement fingerprint at snapshot population time: SHA256 of the
    /// <see cref="MeasurementDefinition"/>'s fields plus its dependent <see
    /// cref="NamedKeyVertex"/>'s fields. Each <see cref="CachedEntry.Measurements"/> value
    /// is tagged with the fingerprint that produced it; a mismatch on load means just
    /// that one measurement is stale and gets recomputed (other measurements survive).</summary>
    public Dictionary<string, string> MeasurementFingerprints { get; set; }
        = new(StringComparer.Ordinal);

    /// <summary>Cached (preset, gender, weight) entries. Lookup at scan time uses
    /// <see cref="CachedEntry.MakeKey"/>.</summary>
    public List<CachedEntry> Entries { get; set; } = new();
}

/// <summary>One (preset, gender, weight) row from a scan, with the input hashes that
/// validate its measurements at reload time.</summary>
public class CachedEntry
{
    public string PresetLabel { get; set; } = "";
    public Gender Gender { get; set; } = Gender.Female;
    public int Weight { get; set; } = 0;

    /// <summary>SHA256 of the canonicalized <see cref="BodySlideSetting.SliderValues"/>
    /// dictionary (slider names sorted, Big/Small values concatenated). Detects slider
    /// modifications including the "preset author released an update" case. Mismatched →
    /// the whole entry is dropped and rescanned end-to-end.</summary>
    public string PresetSliderHash { get; set; } = "";

    public bool TopologyMismatch { get; set; } = false;

    /// <summary>Cached measurement values, one per measurement name. Each carries the
    /// fingerprint that produced it; mismatched fingerprints on load mean that
    /// measurement is recomputed while others in this entry survive.</summary>
    public Dictionary<string, CachedMeasurement> Measurements { get; set; }
        = new(StringComparer.Ordinal);

    public (string PresetLabel, Gender Gender, int Weight) MakeKey()
        => (PresetLabel ?? "", Gender, Weight);
}

/// <summary>One cached measurement value paired with the fingerprint that produced it.
/// Null <see cref="Value"/> distinguishes "the evaluator could not compute this name on
/// this preset/weight" from "the cache doesn't have this name yet" — matches the
/// existing in-memory <c>VM_BodyTypeProfile.MeasurementCacheEntry.Measurements</c>
/// semantics.</summary>
public class CachedMeasurement
{
    public float? Value { get; set; }
    public string Fp { get; set; } = "";
}

/// <summary>Stateless helpers for hashing, file I/O, and in-memory ↔ on-disk conversion.
/// Public so unit tests can exercise the hash determinism without standing up a full
/// editor VM.</summary>
public static class MeasurementCacheStore
{
    /// <summary>Loads a cache file. Returns an empty <see cref="MeasurementCacheData"/>
    /// if the file is absent, corrupt, or version-mismatched — the caller just pays one
    /// rescan rather than the user seeing a hard error. Errors are logged via the
    /// supplied callback when provided.</summary>
    public static MeasurementCacheData Load(string path, Action<string>? logWarn = null)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return new MeasurementCacheData();
        }
        MeasurementCacheData? data;
        try
        {
            data = JSONhandler<MeasurementCacheData>.LoadJSONFile(path, out var ok, out var ex);
            if (!ok || data == null)
            {
                logWarn?.Invoke($"MeasurementCache load failed for {path}: {ex}. Starting empty.");
                return new MeasurementCacheData();
            }
        }
        catch (Exception ex)
        {
            logWarn?.Invoke($"MeasurementCache load threw for {path}: {ex.Message}. Starting empty.");
            return new MeasurementCacheData();
        }
        if (data.Version != 1)
        {
            logWarn?.Invoke($"MeasurementCache schema version {data.Version} unsupported at {path}; expected 1. Starting empty.");
            return new MeasurementCacheData();
        }
        // Defensive: a hand-edited or partial file may have nulls Newtonsoft tolerates.
        data.MeshSnapshots ??= new Dictionary<string, MeshSnapshot>(StringComparer.Ordinal);
        foreach (var snap in data.MeshSnapshots.Values)
        {
            if (snap == null) continue;
            snap.BodyMeshHash ??= "";
            snap.MeasurementFingerprints ??= new Dictionary<string, string>(StringComparer.Ordinal);
            snap.Entries ??= new List<CachedEntry>();
            foreach (var e in snap.Entries)
            {
                if (e == null) continue;
                e.PresetLabel ??= "";
                e.PresetSliderHash ??= "";
                e.Measurements ??= new Dictionary<string, CachedMeasurement>(StringComparer.Ordinal);
            }
        }
        return data;
    }

    /// <summary>Writes the cache to disk. Atomic via temp-file + rename so a mid-write
    /// crash can't leave a half-written cache. The parent directory is created lazily on
    /// first save so installations that never run a scan don't leave an empty folder.</summary>
    public static bool Save(MeasurementCacheData data, string path, Action<string>? logWarn = null)
    {
        if (data == null || string.IsNullOrEmpty(path)) return false;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            JSONhandler<MeasurementCacheData>.SaveJSONFile(data, tmp, out var ok, out var ex);
            if (!ok)
            {
                logWarn?.Invoke($"MeasurementCache save failed for {path}: {ex}");
                if (File.Exists(tmp)) try { File.Delete(tmp); } catch { /* best effort */ }
                return false;
            }
            // Replace atomically.
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            logWarn?.Invoke($"MeasurementCache save threw for {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Standard file-name format. Settings folder convention: one JSON per
    /// profile under <c>SettingsFolder/MeasurementCache/</c>.
    /// <para>Prefers <paramref name="profileName"/> (the user-facing label visible in the
    /// profile dropdown) so the cache directory is human-parseable at a glance — Explorer
    /// shows "CBBE 3BA - Default.measurement_cache.json" rather than a Guid soup. Falls back
    /// to <paramref name="bodyTypeName"/>, then to <paramref name="profileId"/> when the
    /// preferred sources are blank (newly-created profile, or a hand-edited JSON that
    /// dropped the name field).</para>
    /// <para>Inside the file, <see cref="MeasurementCacheData.ProfileId"/> still carries the
    /// Guid for verification — the loader can detect a rename collision (two profiles
    /// renamed to the same string) by mismatched Id, but in practice the user-facing names
    /// are stable enough that collisions are vanishingly rare.</para></summary>
    public static string FilenameFor(string? profileName, string? bodyTypeName, string profileId)
    {
        var pn = (profileName ?? "").Trim();
        var bt = (bodyTypeName ?? "").Trim();
        string baseName;
        if (pn.Length > 0) baseName = pn;
        else if (bt.Length > 0) baseName = bt;
        else baseName = profileId ?? "unnamed";
        return $"{SanitizeFilename(baseName)}.measurement_cache.json";
    }

    /// <summary>Legacy filename format used by the initial cache commit (b033a6f2):
    /// <c>&lt;ProfileId&gt;.measurement_cache.json</c>. Kept so existing caches written
    /// before the human-readable filename change can be located and migrated. The hydrate
    /// path checks the new name first, then this one; if it loads from here, the next save
    /// writes to the new name and the legacy file is deleted in the same operation.</summary>
    public static string LegacyFilenameFor(string profileId)
    {
        return $"{SanitizeFilename(profileId ?? "unnamed")}.measurement_cache.json";
    }

    private static string SanitizeFilename(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unnamed";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(Array.IndexOf(bad, c) < 0 ? c : '_');
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Hash helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>SHA256 of the canonicalized <see cref="BodySlideSetting.SliderValues"/>
    /// dictionary. Sliders are emitted in ordinal-sorted name order so the hash is stable
    /// regardless of insertion order. Empty dictionaries get a fixed sentinel hash so
    /// (a) callers never see a blank string for valid inputs, (b) two empty presets share
    /// a hash and reuse cache entries.</summary>
    public static string ComputePresetSliderHash(BodySlideSetting? preset)
    {
        var sb = new StringBuilder();
        if (preset?.SliderValues != null)
        {
            foreach (var kv in preset.SliderValues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var s = kv.Value;
                sb.Append(kv.Key);
                sb.Append('=');
                sb.Append(s?.Big ?? 0);
                sb.Append('/');
                sb.Append(s?.Small ?? 0);
                sb.Append(';');
            }
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>SHA256 of the current loaded mesh's topology. Input is the
    /// (ShapeName, vertex count) pairs from <c>viewer.GetCurrentShapeVertexCounts()</c>,
    /// ordinal-sorted. Topology changes (vertex count delta, shape add/remove) shift the
    /// hash; pure morph-target adjustments that preserve vertex layout don't — that's the
    /// intended granularity, since topology-preserving morphs are typically intentional
    /// preset edits handled by <see cref="ComputePresetSliderHash"/>.</summary>
    public static string ComputeBodyMeshHash(IReadOnlyDictionary<string, int>? shapeVertexCounts)
    {
        var sb = new StringBuilder();
        if (shapeVertexCounts != null)
        {
            foreach (var kv in shapeVertexCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>SHA256 of a single <see cref="MeasurementDefinition"/> plus its dependent
    /// <see cref="NamedKeyVertex"/>s. Used to mark each cached measurement value with the
    /// fingerprint that produced it; on load, mismatches drop just that measurement (other
    /// measurements in the same entry stay valid). The dependent list pulls every key
    /// vertex referenced by <see cref="MeasurementDefinition.VertexRefNames"/>; ordering
    /// is preserved (the order of refs in the definition is itself semantically
    /// meaningful — A vs B, numerator vs denominator).</summary>
    public static string ComputeMeasurementFingerprint(
        MeasurementDefinition? def,
        IReadOnlyDictionary<string, NamedKeyVertex>? keyVerticesByName)
        => ComputeMeasurementFingerprint(def, keyVerticesByName, null);

    public static string ComputeMeasurementFingerprint(
        MeasurementDefinition? def,
        IReadOnlyDictionary<string, NamedKeyVertex>? keyVerticesByName,
        IReadOnlyDictionary<string, NamedRegion>? regionsByName)
    {
        if (def == null) return "";
        var sb = new StringBuilder();
        sb.Append("N=").Append(def.Name ?? "").Append('|');
        sb.Append("K=").Append((int)def.Kind).Append('|');
        sb.Append("A=").Append((int)def.Axis).Append('|');
        sb.Append("NA=").Append(def.NumeratorAxis.HasValue ? ((int)def.NumeratorAxis.Value).ToString() : "-").Append('|');
        sb.Append("DA=").Append(def.DenominatorAxis.HasValue ? ((int)def.DenominatorAxis.Value).ToString() : "-").Append('|');
        if (def.Kind == MeasurementKind.RegionVolume)
        {
            // RegionVolume reads a single named region's box, not key vertices. Like a BoundingBox
            // key vertex, the defining identity is (ShapeName, box coords, expected cap count) —
            // the resolved patch/loops/caps are session-derived and never enter the fingerprint.
            var regName = def.RegionRefName ?? "";
            sb.Append("RGN=").Append(regName).Append(':');
            if (regionsByName != null && regionsByName.TryGetValue(regName, out var rg) && rg != null)
            {
                AppendRegion(sb, rg);
            }
            else
            {
                sb.Append("MISSING");
            }
        }
        else
        {
            sb.Append("R=[");
            if (def.VertexRefNames != null)
            {
                for (int i = 0; i < def.VertexRefNames.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var refName = def.VertexRefNames[i] ?? "";
                    sb.Append(refName).Append(':');
                    if (keyVerticesByName != null && keyVerticesByName.TryGetValue(refName, out var kv) && kv != null)
                    {
                        AppendKeyVertex(sb, kv);
                    }
                    else
                    {
                        sb.Append("MISSING");
                    }
                }
            }
            sb.Append(']');
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>Pre-builds a {name → fingerprint} map for the supplied profile state.
    /// Caller passes this into the cache-entry validation loop to avoid recomputing
    /// fingerprints per entry.</summary>
    public static Dictionary<string, string> ComputeAllMeasurementFingerprints(
        IReadOnlyList<MeasurementDefinition>? measurements,
        IReadOnlyList<NamedKeyVertex>? keyVertices)
        => ComputeAllMeasurementFingerprints(measurements, keyVertices, null);

    public static Dictionary<string, string> ComputeAllMeasurementFingerprints(
        IReadOnlyList<MeasurementDefinition>? measurements,
        IReadOnlyList<NamedKeyVertex>? keyVertices,
        IReadOnlyList<NamedRegion>? regions)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (measurements == null) return result;
        var kvMap = new Dictionary<string, NamedKeyVertex>(StringComparer.Ordinal);
        if (keyVertices != null)
        {
            foreach (var kv in keyVertices)
            {
                if (kv == null) continue;
                var name = kv.Name?.Trim() ?? "";
                if (name.Length == 0) continue;
                // First-row-wins on duplicate names — matches the editor's duplicate
                // resolution convention so the fingerprint corresponds to the row the
                // evaluator actually uses.
                if (!kvMap.ContainsKey(name)) kvMap[name] = kv;
            }
        }
        var rgMap = new Dictionary<string, NamedRegion>(StringComparer.Ordinal);
        if (regions != null)
        {
            foreach (var rg in regions)
            {
                if (rg == null) continue;
                var name = rg.Name?.Trim() ?? "";
                if (name.Length == 0) continue;
                if (!rgMap.ContainsKey(name)) rgMap[name] = rg; // same first-row-wins discipline
            }
        }
        foreach (var def in measurements)
        {
            if (def == null || string.IsNullOrEmpty(def.Name)) continue;
            if (result.ContainsKey(def.Name)) continue; // same first-row-wins discipline
            result[def.Name] = ComputeMeasurementFingerprint(def, kvMap, rgMap);
        }
        return result;
    }

    private static void AppendKeyVertex(StringBuilder sb, NamedKeyVertex kv)
    {
        sb.Append("S=").Append(kv.ShapeName ?? "").Append('|');
        sb.Append("St=").Append((int)kv.Strategy).Append('|');
        // VertexIndex is part of the defining identity for Explicit strategy (the user
        // typed a specific vertex index and the rule's value depends on it). For
        // BoundingBox strategy, VertexIndex is a runtime-resolved cache — the box gets
        // matched against the current mesh and the winning vertex's index is stored on
        // the model. Different sessions can re-resolve the same box to a neighbor vertex
        // (different mesh state at resolution time, float precision in the box scan)
        // even when the user changed nothing. Including it in the fingerprint then
        // produced spurious "drift" that invalidated every BB-dependent measurement on
        // every restart. The defining identity for BB is (ShapeName, box coords,
        // Criterion, Strategy) — the index is just the most recent resolution result.
        if (kv.Strategy == KeyVertexStrategy.Explicit)
        {
            sb.Append("I=").Append(kv.VertexIndex).Append('|');
        }
        sb.Append("Cr=").Append((int)kv.Criterion).Append('|');
        sb.Append("B=").Append(kv.BoxMinX).Append(',').Append(kv.BoxMinY).Append(',').Append(kv.BoxMinZ);
        sb.Append('-').Append(kv.BoxMaxX).Append(',').Append(kv.BoxMaxY).Append(',').Append(kv.BoxMaxZ);
    }

    private static void AppendRegion(StringBuilder sb, NamedRegion rg)
    {
        // Defining identity for a RegionVolume measurement: shape, box coords, and expected cap
        // count. The resolved surface patch / boundary loops / cap topology are recomputed each
        // session from (box + sliders-0 mesh) and are deliberately excluded — same rationale as
        // excluding a BoundingBox key vertex's resolved VertexIndex (commit 5e9610a7): they can
        // re-resolve to equivalent-but-different topology across sessions without any authoring
        // change, and hashing them would spuriously invalidate every region volume on restart.
        sb.Append("S=").Append(rg.ShapeName ?? "").Append('|');
        sb.Append("CC=").Append(rg.ExpectedCapCount.HasValue ? rg.ExpectedCapCount.Value.ToString() : "-").Append('|');
        // CapMode IS part of the identity: unlike the box, it changes the computed volume (flat-plane
        // cut vs anatomical-fan cap), so a mode change must invalidate the cached value.
        sb.Append("CM=").Append((int)rg.CapMode).Append('|');
        sb.Append("B=").Append(rg.BoxMinX).Append(',').Append(rg.BoxMinY).Append(',').Append(rg.BoxMinZ);
        sb.Append('-').Append(rg.BoxMaxX).Append(',').Append(rg.BoxMaxY).Append(',').Append(rg.BoxMaxZ);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input ?? "");
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
