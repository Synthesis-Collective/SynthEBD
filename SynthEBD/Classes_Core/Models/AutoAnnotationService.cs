using System.IO;

namespace SynthEBD;

/// <summary>
/// Supplies the inputs <see cref="AutoAnnotationEngine"/> needs -- the body type's active profile and
/// each preset slot's cached measurements -- and runs it for the three on-demand consumers: the
/// BodySlides preset editor (when a preset is selected), the pre-patch step, and the BodySlides
/// menu's Descriptor Filter. Automatic assignments are never stored, so nothing here is kept in sync
/// in the background.
///
/// <para><b>Profiles</b> come from <see cref="LiveProfileProvider"/> when the Auto-Annotation editor is
/// alive (it holds unsaved rule edits), else from <see cref="Settings_OBody.BodyTypeProfiles"/>.</para>
///
/// <para><b>Measurements</b> come from <see cref="LiveCacheProvider"/> (the editor's in-memory cache)
/// when it has entries for the body type, else from the profile's on-disk measurement cache, loaded on
/// first use and reloaded when the file changes. Disk values are validated like the editor's own
/// hydrate: a value whose measurement fingerprint drifted is dropped, and an entry whose preset slider
/// hash no longer matches the preset is ignored (the preset was edited since it was measured).</para>
/// </summary>
public class AutoAnnotationService
{
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    private readonly object _diskCacheLock = new();
    private readonly Dictionary<string, DiskCache> _diskCaches = new(StringComparer.OrdinalIgnoreCase);

    public AutoAnnotationService(PatcherState patcherState, SynthEBDPaths paths, Logger logger)
    {
        _patcherState = patcherState;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>One slot's cached measurements.</summary>
    public sealed class SlotMeasurements
    {
        public Dictionary<string, float> Values { get; } = new(StringComparer.Ordinal);
        /// <summary>Every measurement the entry carries, including failed ones.</summary>
        public HashSet<string> Known { get; } = new(StringComparer.Ordinal);
        public string PresetSliderHash { get; set; } = "";
    }

    /// <summary>Returns the live (possibly unsaved) profile for a body type, or null when the
    /// Auto-Annotation editor has none. Wired by the OBody settings VM.</summary>
    public Func<string, BodyTypeProfile?>? LiveProfileProvider { get; set; }

    /// <summary>Returns a lookup into the editor's in-memory measurement cache for a body type, or null
    /// when it has none loaded. Wired by the OBody settings VM.</summary>
    public Func<string, Func<(string PresetLabel, Gender Gender, int Weight), SlotMeasurements?>?>? LiveCacheProvider { get; set; }

    /// <summary>The active profile for <paramref name="preset"/>'s body type, or null.</summary>
    public BodyTypeProfile? ResolveProfile(BodySlideSetting? preset, bool useLiveSources = true)
    {
        var bodyType = preset?.SliderGroup;
        if (string.IsNullOrWhiteSpace(bodyType)) return null;
        try
        {
            var live = useLiveSources ? LiveProfileProvider?.Invoke(bodyType) : null;
            if (live != null) return live;
        }
        catch (Exception ex)
        {
            _logger.LogError("Auto-Annotation: could not read the live profile for " + bodyType + ": " + ex.Message);
        }
        return _patcherState.OBodySettings?.BodyTypeProfiles?
            .FirstOrDefault(p => p != null && string.Equals(p.BodyTypeName, bodyType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Automatic descriptors for every weight slot of <paramref name="preset"/>, keyed by slot weight.
    /// Empty when the preset has no profile, or Auto-Annotation is turned off. May load the measurement
    /// cache from disk on first use. <paramref name="useLiveSources"/> = false reads only saved state
    /// (settings profiles and disk caches), never the editor's view models -- required off the UI thread.
    /// </summary>
    public Dictionary<int, AutoAnnotationEngine.SlotResult> DeriveForPreset(BodySlideSetting preset, Gender gender, BodyTypeProfile? profile = null, bool useLiveSources = true)
    {
        var output = new Dictionary<int, AutoAnnotationEngine.SlotResult>();
        if (preset?.BodyShapeDescriptorsByWeight == null) return output;
        if (_patcherState.OBodySettings != null && !_patcherState.OBodySettings.AutoApplyMissingAnnotations) return output;
        profile ??= ResolveProfile(preset, useLiveSources);
        if (profile == null) return output;

        var cache = useLiveSources ? GetCache(profile) : GetDiskCache(profile);
        string sliderHash = MeasurementCacheStore.ComputePresetSliderHash(preset);
        foreach (var weight in preset.BodyShapeDescriptorsByWeight.Keys)
        {
            SlotMeasurements? slot = null;
            var entry = cache?.Invoke((preset.Label ?? "", gender, weight));
            if (entry != null
                && (string.IsNullOrEmpty(entry.PresetSliderHash) || string.Equals(entry.PresetSliderHash, sliderHash, StringComparison.Ordinal)))
            {
                slot = entry;
            }
            output[weight] = AutoAnnotationEngine.DeriveForSlot(profile, preset, gender, weight,
                slot?.Values, slot?.Known);
        }
        return output;
    }

    /// <summary>
    /// Pre-patch step: writes every preset's automatic descriptors into its own slots as
    /// <see cref="BodyShapeAnnotationSource.Classifier"/> entries, after removing any non-Manual entry.
    /// Callers must pass the patcher's working copies, never the settings the UI edits.
    /// Returns the number of descriptors added.
    /// </summary>
    public int ApplyToPresetCopies(IEnumerable<BodySlideSetting> presets, Gender gender)
    {
        int added = 0;
        foreach (var preset in presets ?? Enumerable.Empty<BodySlideSetting>())
        {
            if (preset?.BodyShapeDescriptorsByWeight == null) continue;
            preset.StripNonManualDescriptors();
            foreach (var (weight, result) in DeriveForPreset(preset, gender, useLiveSources: false))
            {
                if (!preset.BodyShapeDescriptorsByWeight.TryGetValue(weight, out var slot) || slot == null) continue;
                foreach (var d in result.Descriptors)
                {
                    if (slot.Add(new AnnotatedDescriptorSignature(d, BodyShapeAnnotationSource.Classifier))) added++;
                }
            }
        }
        return added;
    }

    /// <summary>The union across all slots of <paramref name="preset"/>'s stored Manual descriptors and
    /// its automatic ones -- what the patcher will see. Used by the BodySlides menu's Descriptor Filter
    /// and "remove unannotated". Pass a <paramref name="profile"/> resolved on the UI thread and
    /// <paramref name="useLiveSources"/> = false when calling from a background thread.</summary>
    public HashSet<BodyShapeDescriptor.LabelSignature> GetEffectiveDescriptorUnion(BodySlideSetting preset, Gender gender, BodyTypeProfile? profile = null, bool useLiveSources = true)
    {
        var result = new HashSet<BodyShapeDescriptor.LabelSignature>();
        foreach (var d in preset.EnumerateAllDescriptors())
        {
            if (d != null && d.Source == BodyShapeAnnotationSource.Manual) result.Add(d.ToLabelSignature());
        }
        foreach (var slot in DeriveForPreset(preset, gender, profile, useLiveSources).Values)
        {
            result.UnionWith(slot.Descriptors);
        }
        return result;
    }

    /// <summary>Loads <paramref name="profile"/>'s on-disk measurement cache if it isn't loaded yet (or
    /// changed on disk). Safe off the UI thread; lets a UI caller do the slow part in the background and
    /// then derive on the UI thread, where the editor's live cache may be read.</summary>
    public void WarmDiskCache(BodyTypeProfile? profile)
    {
        if (profile != null) LoadDiskCache(profile);
    }

    /// <summary>True when the Auto-Annotation editor holds <paramref name="profile"/>'s measurements in
    /// memory, so the cache file needn't be loaded. UI thread only.</summary>
    public bool HasLiveCache(BodyTypeProfile? profile)
        => profile != null && LiveCacheProvider?.Invoke(profile.BodyTypeName) != null;

    /// <summary>Forgets the loaded disk caches, e.g. after the editor wrote a cache file.</summary>
    public void InvalidateDiskCaches()
    {
        lock (_diskCacheLock) _diskCaches.Clear();
    }

    private Func<(string PresetLabel, Gender Gender, int Weight), SlotMeasurements?>? GetCache(BodyTypeProfile profile)
    {
        try
        {
            var live = LiveCacheProvider?.Invoke(profile.BodyTypeName);
            if (live != null) return live;
        }
        catch (Exception ex)
        {
            _logger.LogError("Auto-Annotation: could not read the editor's measurement cache: " + ex.Message);
        }
        return GetDiskCache(profile);
    }

    private sealed class DiskCache
    {
        public string Path = "";
        public DateTime LastWriteUtc;
        public string FingerprintKey = "";
        public Dictionary<(string PresetLabel, Gender Gender, int Weight), SlotMeasurements> Entries = new();
    }

    private Func<(string PresetLabel, Gender Gender, int Weight), SlotMeasurements?>? GetDiskCache(BodyTypeProfile profile)
    {
        var entries = LoadDiskCache(profile);
        if (entries == null) return null;
        return key => entries.TryGetValue(key, out var slot) ? slot : null;
    }

    private Dictionary<(string PresetLabel, Gender Gender, int Weight), SlotMeasurements>? LoadDiskCache(BodyTypeProfile profile)
    {
        if (profile == null || string.IsNullOrEmpty(profile.Id) || string.IsNullOrWhiteSpace(profile.BodyTypeName)) return null;
        if (profile.Measurements == null || profile.Measurements.Count == 0) return null;

        string dir = _paths.MeasurementCacheDirPath;
        string path = System.IO.Path.Combine(dir, MeasurementCacheStore.FilenameFor(profile.Name, profile.BodyTypeName, profile.Id));
        if (!File.Exists(path))
        {
            string legacy = System.IO.Path.Combine(dir, MeasurementCacheStore.LegacyFilenameFor(profile.Id));
            if (!File.Exists(legacy)) return null;
            path = legacy;
        }

        var currentFps = MeasurementCacheStore.ComputeAllMeasurementFingerprints(profile.Measurements, profile.KeyVertices, profile.Regions);
        string fpKey = string.Join(";", currentFps.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
        DateTime lastWrite = File.GetLastWriteTimeUtc(path);

        lock (_diskCacheLock)
        {
            if (_diskCaches.TryGetValue(profile.BodyTypeName, out var existing)
                && existing.Path == path && existing.LastWriteUtc == lastWrite && existing.FingerprintKey == fpKey)
            {
                return existing.Entries;
            }

            var loaded = new DiskCache { Path = path, LastWriteUtc = lastWrite, FingerprintKey = fpKey };
            var data = MeasurementCacheStore.Load(path, _logger.LogMessage);
            if (data?.MeshSnapshots != null
                && data.MeshSnapshots.TryGetValue(profile.BodyTypeName.Trim(), out var snapshot)
                && snapshot?.Entries != null)
            {
                foreach (var entry in snapshot.Entries)
                {
                    if (entry?.Measurements == null) continue;
                    var slot = new SlotMeasurements { PresetSliderHash = entry.PresetSliderHash ?? "" };
                    foreach (var (name, cached) in entry.Measurements)
                    {
                        if (cached == null) continue;
                        if (!currentFps.TryGetValue(name, out var fp) || !string.Equals(cached.Fp ?? "", fp, StringComparison.Ordinal)) continue;
                        slot.Known.Add(name);
                        if (cached.Value.HasValue) slot.Values[name] = cached.Value.Value;
                    }
                    loaded.Entries[entry.MakeKey()] = slot;
                }
            }
            _diskCaches[profile.BodyTypeName] = loaded;
            _logger.LogMessage($"Auto-Annotation: loaded {loaded.Entries.Count} cached measurement slices for {profile.BodyTypeName} from {path}.");
            return loaded.Entries;
        }
    }
}
