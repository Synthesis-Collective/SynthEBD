using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// UI editor for <see cref="Settings_OBody.BodyTypeProfiles"/>. Hosts a list of
/// <see cref="VM_BodyTypeProfile"/> rows; the selected profile drives the right-side panels
/// (key vertices, measurements, rules, labeled examples). Owns a dedicated
/// <see cref="VM_CharacterViewer"/> + searchable BodySlide preset picker so the user can
/// author profiles without flipping back to the BodySlides menu.
/// </summary>
public class VM_BodyTypeProfileEditor : VM
{
    public delegate VM_BodyTypeProfileEditor Factory();

    private readonly Logger _logger;
    private readonly Func<VM_SettingsOBody> _oBodyVM;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;

    public VM_BodyTypeProfileEditor(
        Logger logger,
        Func<VM_CharacterViewer> characterViewerFactory,
        Func<VM_SettingsOBody> oBodyVM,
        IEnvironmentStateProvider environmentProvider,
        PatcherState patcherState)
    {
        _logger = logger;
        _oBodyVM = oBodyVM;
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;

        CharacterViewer = characterViewerFactory();
        CharacterViewer.Mode = ViewerMode.ReadOnly;
        CharacterViewer.ShowClassifierControls = true;
        CharacterViewer.DisposeWith(this);

        // ApplyBodySlide may defer to _pendingBodySlide when the scene isn't yet rebuilt
        // (LoadNpcAsync returns before ProcessPendingScene runs on the GL thread). The
        // drain path runs later and calls ApplyBodySlide internally — RefreshPreviewAsync's
        // in-line RefreshMeasurementValues would have already run on an empty scene by then.
        // Subscribing here guarantees a post-deform recompute regardless of path.
        CharacterViewer.BodySlideApplied += () => SelectedProfile?.RefreshMeasurementValues();

        AvailableWeights = new ObservableCollection<int> { 0, 25, 50, 75, 100 };

        AddProfile = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var profile = new VM_BodyTypeProfile(new BodyTypeProfile { Name = "New Profile" }, this);
                Profiles.Add(profile);
                SelectedProfile = profile;
            });

        DeleteSelectedProfile = new RelayCommand(
            canExecute: _ => SelectedProfile != null,
            execute: _ =>
            {
                var p = SelectedProfile;
                if (p == null) return;
                int idx = Profiles.IndexOf(p);
                Profiles.Remove(p);
                SelectedProfile = Profiles.Count == 0
                    ? null
                    : Profiles[Math.Min(idx, Profiles.Count - 1)];
            });

        ExportSelectedProfile = new RelayCommand(
            canExecute: _ => SelectedProfile != null,
            execute: _ => ExportProfile(SelectedProfile));

        ImportProfile = new RelayCommand(
            canExecute: _ => true,
            execute: _ => DoImportProfile());

        RefreshPresetList = new RelayCommand(
            canExecute: _ => true,
            execute: _ => RebuildAvailablePresets());

        VM_CharacterViewer.AnyKeyVertexPicked += OnAnyKeyVertexPicked;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(PreviewGender):
                    RebuildAvailablePresets();
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(PresetFilterText):
                    RebuildFilteredPresets();
                    break;
                case nameof(SelectedPreset):
                case nameof(PreviewWeight):
                case nameof(PreviewNpcOverride):
                    _ = RefreshPreviewAsync();
                    break;
                case nameof(SelectedProfile):
                    if (SelectedProfile != null)
                    {
                        SelectedProfile.AttachViewer(CharacterViewer);
                        SelectedProfile.RefreshMeasurementValues();
                    }
                    break;
            }
        };

        // Preset list is populated on first view Loaded (see UC_BodyTypeProfileEditor.xaml.cs):
        // calling _oBodyVM() here would re-enter VM_SettingsOBody's ctor, which depends on
        // this editor, causing a DI stack overflow.
    }

    public ObservableCollection<VM_BodyTypeProfile> Profiles { get; } = new();
    public VM_BodyTypeProfile? SelectedProfile { get; set; }

    /// <summary>Body-type names sourced from <see cref="Settings_OBody.BodyTypeRegistry"/> so the per-profile dropdown stays consistent with the registry editor.</summary>
    public ObservableCollection<string> AvailableBodyTypeNames { get; } = new();

    /// <summary>Descriptor signatures available for use in rules/labeled examples. Sourced from <see cref="Settings_OBody.TemplateDescriptors"/>.</summary>
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors { get; } = new();

    public RelayCommand AddProfile { get; }
    public RelayCommand DeleteSelectedProfile { get; }
    public RelayCommand ExportSelectedProfile { get; }
    public RelayCommand ImportProfile { get; }
    public RelayCommand RefreshPresetList { get; }

    /// <summary>Dedicated 3D viewer embedded in the editor. Drives both preset preview
    /// and vertex picking so the user does not have to flip over to the BodySlides menu.</summary>
    public VM_CharacterViewer CharacterViewer { get; }

    /// <summary>All presets sourced from <see cref="VM_SettingsOBody.BodySlidesUI"/>, filtered
    /// by <see cref="PreviewGender"/>. Rebuilt on demand via <see cref="RefreshPresetList"/>.</summary>
    public ObservableCollection<VM_BodySlidePlaceHolder> AvailablePresets { get; } = new();

    /// <summary>Substring-filtered view of <see cref="AvailablePresets"/> driven by
    /// <see cref="PresetFilterText"/>. Bound to the searchable picker.</summary>
    public ObservableCollection<VM_BodySlidePlaceHolder> FilteredPresets { get; } = new();

    public ObservableCollection<int> AvailableWeights { get; }

    public string PresetFilterText { get; set; } = "";
    public VM_BodySlidePlaceHolder? SelectedPreset { get; set; }
    public Gender PreviewGender { get; set; } = Gender.Female;
    public int PreviewWeight { get; set; } = 50;

    /// <summary>Optional NPC override. When null, the configured per-weight preview NPC is used
    /// (same policy as <see cref="VM_BodySlideSetting.RefreshPreview"/>).</summary>
    public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;

    /// <summary>Exposed for the NPC picker's scoped-types filter.</summary>
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    /// <summary>Current environment link cache; bound by the NPC picker's LinkCache. Public
    /// so the XAML FormKeyPicker can resolve candidate NPC records.</summary>
    public ILinkCache? lk { get; private set; }

    public override void Dispose()
    {
        VM_CharacterViewer.AnyKeyVertexPicked -= OnAnyKeyVertexPicked;
        base.Dispose();
    }

    public void CopyInViewModelFromModel(Settings_OBody model)
    {
        Profiles.Clear();
        AvailableBodyTypeNames.Clear();
        AvailableDescriptors.Clear();

        if (model == null) return;

        if (model.BodyTypeRegistry != null)
        {
            foreach (var entry in model.BodyTypeRegistry)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                if (!AvailableBodyTypeNames.Contains(entry.Name)) AvailableBodyTypeNames.Add(entry.Name);
            }
        }

        if (model.TemplateDescriptors != null)
        {
            foreach (var d in model.TemplateDescriptors)
            {
                if (d?.ID == null) continue;
                AvailableDescriptors.Add(new BodyShapeDescriptor.LabelSignature { Category = d.ID.Category, Value = d.ID.Value });
            }
        }

        if (model.BodyTypeProfiles != null)
        {
            foreach (var p in model.BodyTypeProfiles)
            {
                if (p == null) continue;
                Profiles.Add(new VM_BodyTypeProfile(p, this));
            }
        }

        SelectedProfile = Profiles.FirstOrDefault();
    }

    public void DumpViewModelToModel(Settings_OBody model)
    {
        if (model == null) return;
        model.BodyTypeProfiles = new List<BodyTypeProfile>();
        foreach (var vm in Profiles)
        {
            model.BodyTypeProfiles.Add(vm.DumpToModel());
        }
    }

    private void ExportProfile(VM_BodyTypeProfile? profile)
    {
        if (profile == null) return;
        var model = profile.DumpToModel();
        string defaultName = string.IsNullOrWhiteSpace(model.Name) ? "BodyTypeProfile.json" : SanitizeFileName(model.Name) + ".json";
        if (!IO_Aux.SelectFileSave("", "BodyType Profile (*.json)|*.json", ".json", "Export BodyType Profile", out string path, defaultName))
        {
            return;
        }
        JSONhandler<BodyTypeProfile>.SaveJSONFile(model, path, out bool success, out string exception);
        if (!success)
        {
            MessageWindow.DisplayNotificationOK("Export Failed", exception);
            return;
        }
        _logger?.LogMessage("BodyTypeProfileEditor: exported profile '" + model.Name + "' to " + path);
    }

    private void DoImportProfile()
    {
        if (!IO_Aux.SelectFile("", "BodyType Profile (*.json)|*.json", "Import BodyType Profile", out string path))
        {
            return;
        }
        var loaded = JSONhandler<BodyTypeProfile>.LoadJSONFile(path, out bool success, out string exception);
        if (!success || loaded == null)
        {
            MessageWindow.DisplayNotificationOK("Import Failed", exception);
            return;
        }

        // Always assign a fresh Id so imported profiles don't collide with existing ones.
        loaded.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(loaded.Name)) loaded.Name = "Imported Profile";

        var existingNames = new HashSet<string>(Profiles.Select(p => p.Name ?? ""), StringComparer.OrdinalIgnoreCase);
        string baseName = loaded.Name;
        int suffix = 2;
        while (existingNames.Contains(loaded.Name))
        {
            loaded.Name = baseName + " (" + suffix + ")";
            suffix++;
        }

        var vm = new VM_BodyTypeProfile(loaded, this);
        Profiles.Add(vm);
        SelectedProfile = vm;
        _logger?.LogMessage("BodyTypeProfileEditor: imported profile '" + loaded.Name + "' from " + path);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private void OnAnyKeyVertexPicked(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexPick pick)
    {
        // Scope picks to the editor's own viewer so the BodySlides-menu viewer doesn't
        // bleed into profile capture when both menus are open.
        if (!ReferenceEquals(viewer, CharacterViewer)) return;

        var profile = SelectedProfile;
        if (profile == null || !profile.CapturePicks) return;
        profile.OnVertexPickedFromViewer(viewer, pick);
    }

    /// <summary>
    /// Rebuilds <see cref="AvailablePresets"/> from the matching gender's BodySlide list
    /// on <see cref="VM_SettingsOBody.BodySlidesUI"/>. Also refreshes
    /// <see cref="FilteredPresets"/> so the picker reflects any current filter text.
    /// Safe to call before the BodySlides menu has been constructed — no-op in that case.
    /// </summary>
    public void RebuildAvailablePresets()
    {
        AvailablePresets.Clear();
        var menu = _oBodyVM?.Invoke()?.BodySlidesUI;
        if (menu == null)
        {
            RebuildFilteredPresets();
            return;
        }

        var source = PreviewGender == Gender.Male ? menu.BodySlidesMale : menu.BodySlidesFemale;
        if (source != null)
        {
            foreach (var ph in source.OrderBy(p => p?.Label ?? "", StringComparer.OrdinalIgnoreCase))
            {
                if (ph == null) continue;
                AvailablePresets.Add(ph);
            }
        }
        RebuildFilteredPresets();
    }

    private void RebuildFilteredPresets()
    {
        FilteredPresets.Clear();
        string filter = PresetFilterText?.Trim() ?? "";
        bool hasFilter = filter.Length > 0;
        foreach (var ph in AvailablePresets)
        {
            if (!hasFilter || (ph.Label != null && ph.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                FilteredPresets.Add(ph);
            }
        }
    }

    /// <summary>
    /// Loads the configured preview NPC for the current weight and applies the selected
    /// preset's BodySlide deformation. Mirrors <see cref="VM_BodySlideSetting.RefreshPreview"/>'s
    /// NPC-resolution policy so behavior matches the main BodySlides menu.
    /// </summary>
    private async System.Threading.Tasks.Task RefreshPreviewAsync()
    {
        try
        {
            var preset = SelectedPreset?.AssociatedModel;
            if (preset == null || lk == null) return;

            FormKey npc = FormKey.Null;
            if (!PreviewNpcOverride.IsNull)
            {
                npc = PreviewNpcOverride;
            }
            else
            {
                var preview = _patcherState?.OBodySettings?.PreviewNpcs;
                if (preview != null && preview.WeightPreviewNpcs.TryGetValue(PreviewWeight, out var pair) && pair != null)
                {
                    npc = PreviewGender == Gender.Female ? pair.FemaleNpc : pair.MaleNpc;
                }
            }

            if (npc.IsNull)
            {
                _logger?.LogMessage("BodyTypeProfileEditor: no preview NPC configured for weight " + PreviewWeight + " (" + PreviewGender + ")");
                return;
            }

            await CharacterViewer.LoadNpcAsync(npc, lk);
            CharacterViewer.ApplyBodySlide(preset, PreviewWeight);

            // Point the active profile at this viewer so live measurement readouts have
            // a source and pick-capture routes into the right profile by default.
            if (SelectedProfile != null)
            {
                SelectedProfile.AttachViewer(CharacterViewer);
                SelectedProfile.RefreshMeasurementValues();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError("BodyTypeProfileEditor.RefreshPreviewAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
        }
    }

    /// <summary>Logs a one-line diagnostic to the patcher's main log. Used by sub-VMs (suggest pass, etc.).</summary>
    internal void LogMessage(string message)
    {
        _logger?.LogMessage("BodyTypeProfileEditor: " + message);
    }
}

/// <summary>
/// Single-profile editor. Owns sub-collections for key vertices, measurements, rules, and
/// labeled examples. Knows how to compute live measurement values against an attached viewer's
/// current mesh state via <see cref="VM_CharacterViewer.TryGetCurrentVertex"/>.
/// </summary>
public class VM_BodyTypeProfile : VM
{
    private readonly BodyTypeProfile _source;
    private readonly VM_BodyTypeProfileEditor _parent;

    public VM_BodyTypeProfile(BodyTypeProfile source, VM_BodyTypeProfileEditor parent)
    {
        _source = source ?? new BodyTypeProfile();
        _parent = parent;

        Id = _source.Id;
        Name = _source.Name ?? "";
        BodyTypeName = _source.BodyTypeName ?? "";

        FingerprintVertexCount = _source.Fingerprint?.VertexCount ?? 0;
        FingerprintShapeCounts = _source.Fingerprint?.ShapeVertexCounts != null
            ? string.Join(", ", _source.Fingerprint.ShapeVertexCounts.Select(kv => kv.Key + ":" + kv.Value))
            : "";

        if (_source.KeyVertices != null)
        {
            foreach (var k in _source.KeyVertices)
            {
                if (k == null) continue;
                KeyVertices.Add(new VM_NamedKeyVertex(k, this));
            }
        }
        if (_source.Measurements != null)
        {
            foreach (var m in _source.Measurements)
            {
                if (m == null) continue;
                Measurements.Add(new VM_MeasurementDefinition(m, this));
            }
        }
        if (_source.Rules != null)
        {
            foreach (var r in _source.Rules)
            {
                if (r == null) continue;
                Rules.Add(new VM_MeasurementRule(r, this));
            }
        }
        if (_source.LabeledExamples != null)
        {
            foreach (var l in _source.LabeledExamples)
            {
                if (l == null) continue;
                LabeledExamples.Add(new VM_LabeledExample(l, this));
            }
        }

        AddMeasurement = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Measurements.Add(new VM_MeasurementDefinition(new MeasurementDefinition { Name = NextDefaultName("Measurement", Measurements.Select(x => x.Name)) }, this)));

        AddRule = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                var rule = new MeasurementRule { Descriptor = new BodyShapeDescriptor.LabelSignature() };
                rule.GroupsORlogic.Add(new AndGatedMeasurementGroup());
                Rules.Add(new VM_MeasurementRule(rule, this));
            });

        CaptureFingerprintFromActiveViewer = new RelayCommand(
            canExecute: _ => ActiveViewer != null,
            execute: _ =>
            {
                if (ActiveViewer == null) return;
                var counts = ActiveViewer.GetCurrentShapeVertexCounts();
                FingerprintVertexCount = counts.Values.Sum();
                FingerprintShapeCounts = string.Join(", ", counts.Select(kv => kv.Key + ":" + kv.Value));
            });

        SuggestThresholds = new RelayCommand(
            canExecute: _ => SelectedDescriptorForLabeling != null && LabeledExamples.Any(),
            execute: _ => RunSuggestThresholds());

        RemoveSelectedKeyVertex = new RelayCommand(
            canExecute: _ => SelectedKeyVertex != null,
            execute: _ =>
            {
                var k = SelectedKeyVertex;
                if (k == null) return;
                KeyVertices.Remove(k);
            });

        // Re-evaluate live values whenever the measurement collection changes shape or
        // any row's definition fields (Kind / Axis / VertexRefA..D) are edited in the grid.
        foreach (var m in Measurements) m.PropertyChanged += OnMeasurementRowPropertyChanged;
        Measurements.CollectionChanged += (_, args) =>
        {
            if (args.OldItems != null)
                foreach (VM_MeasurementDefinition m in args.OldItems) m.PropertyChanged -= OnMeasurementRowPropertyChanged;
            if (args.NewItems != null)
                foreach (VM_MeasurementDefinition m in args.NewItems) m.PropertyChanged += OnMeasurementRowPropertyChanged;
            RefreshMeasurementValues();
        };

        // Re-evaluate when the key-vertex roster changes (a measurement may reference a
        // newly-added vertex name, or lose a deleted one).
        KeyVertices.CollectionChanged += (_, __) => RefreshMeasurementValues();

        RefreshMeasurementValues();

        // Repaint the measurement-line overlay whenever the user picks a different
        // measurement. Using the raw PropertyChanged event keeps this file free of
        // additional ReactiveUI wiring (the profile's lifetime is bounded by the
        // containing editor, so we skip the IDisposable dance).
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedMeasurement))
            {
                RefreshMeasurementHighlight();
            }
        };
    }

    private void OnMeasurementRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // LiveValue updates are the result of recomputation; re-running on that would loop.
        if (e.PropertyName == nameof(VM_MeasurementDefinition.LiveValue)) return;
        RefreshMeasurementValues();
    }

    public string Id { get; }
    public string Name { get; set; }
    public string BodyTypeName { get; set; }

    public int FingerprintVertexCount { get; set; }
    public string FingerprintShapeCounts { get; set; }

    public ObservableCollection<VM_NamedKeyVertex> KeyVertices { get; } = new();
    public ObservableCollection<VM_MeasurementDefinition> Measurements { get; } = new();
    public ObservableCollection<VM_MeasurementRule> Rules { get; } = new();
    public ObservableCollection<VM_LabeledExample> LabeledExamples { get; } = new();

    public VM_NamedKeyVertex? SelectedKeyVertex { get; set; }

    /// <summary>Currently highlighted measurement. Drives the colored-line overlay in the active viewer.</summary>
    public VM_MeasurementDefinition? SelectedMeasurement { get; set; }

    /// <summary>When true, key-vertex picks from any viewer add a new entry to this profile.</summary>
    public bool CapturePicks { get; set; } = false;

    /// <summary>Most recent viewer to fire a pick targeting this profile. Used for live measurement readouts.</summary>
    public VM_CharacterViewer? ActiveViewer { get; private set; }

    /// <summary>Binds this profile to the supplied viewer so live readouts and the
    /// measurement-line overlay target the right scene. Called by the editor when a
    /// preset is loaded in its embedded viewer.</summary>
    public void AttachViewer(VM_CharacterViewer viewer)
    {
        ActiveViewer = viewer;
    }

    /// <summary>Descriptor the user is currently labeling examples for in suggest mode.</summary>
    public BodyShapeDescriptor.LabelSignature? SelectedDescriptorForLabeling { get; set; }

    public RelayCommand AddMeasurement { get; }
    public RelayCommand AddRule { get; }
    public RelayCommand CaptureFingerprintFromActiveViewer { get; }
    public RelayCommand SuggestThresholds { get; }
    public RelayCommand RemoveSelectedKeyVertex { get; }

    public IEnumerable<string> AvailableMeasurementNames => Measurements.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n));
    public IEnumerable<string> AvailableKeyVertexNames => KeyVertices.Select(k => k.Name).Where(n => !string.IsNullOrEmpty(n));

    public ObservableCollection<string> AvailableBodyTypeNames => _parent.AvailableBodyTypeNames;
    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    public void OnVertexPickedFromViewer(VM_CharacterViewer viewer, VM_CharacterViewer.KeyVertexPick pick)
    {
        ActiveViewer = viewer;

        var model = new NamedKeyVertex
        {
            Name = NextDefaultName("KV", KeyVertices.Select(k => k.Name)),
            ShapeName = pick.Mesh?.ShapeName ?? "",
            VertexIndex = pick.VertexIndex,
        };
        var vm = new VM_NamedKeyVertex(model, this);
        KeyVertices.Add(vm);
        SelectedKeyVertex = vm;

        RefreshMeasurementValues();
    }

    /// <summary>
    /// Re-evaluates every measurement against <see cref="ActiveViewer"/> and writes the
    /// result back into each <see cref="VM_MeasurementDefinition.LiveValue"/>. Called after
    /// any structural change (vertex add, measurement edit) and externally when the viewer's
    /// preset/weight changes.
    /// </summary>
    public void RefreshMeasurementValues()
    {
        if (Measurements.Count == 0)
        {
            RefreshMeasurementHighlight();
            return;
        }

        var keyVertsByName = KeyVertices
            .Where(k => !string.IsNullOrEmpty(k.Name))
            .GroupBy(k => k.Name)
            .ToDictionary(g => g.Key, g => g.First().DumpToModel(), StringComparer.Ordinal);

        var viewer = ActiveViewer;

        foreach (var m in Measurements)
        {
            if (viewer == null)
            {
                m.LiveValue = null;
                continue;
            }
            if (MeasurementMath.TryEvaluate(m.DumpToModel(), keyVertsByName,
                (shape, idx) => viewer.TryGetCurrentVertex(shape, idx, out var p) ? (OpenTK.Mathematics.Vector3?)p : null,
                out float v))
            {
                m.LiveValue = v;
            }
            else
            {
                m.LiveValue = null;
            }
        }

        RefreshMeasurementHighlight();
    }

    /// <summary>
    /// Pushes line segments for <see cref="SelectedMeasurement"/> into the active viewer's
    /// measurement-line overlay. PointDistance/AxisDistance render one A-B segment;
    /// RatioDistance renders two segments (A-B in the numerator color, C-D in the
    /// denominator color). Clears the overlay when there is no selection, no viewer, or
    /// when the referenced vertices cannot be resolved.
    /// </summary>
    private void RefreshMeasurementHighlight()
    {
        var viewer = ActiveViewer;
        if (viewer == null) return;

        var sel = SelectedMeasurement;
        if (sel == null)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        var keyVertsByName = KeyVertices
            .Where(k => !string.IsNullOrEmpty(k.Name))
            .GroupBy(k => k.Name)
            .ToDictionary(g => g.Key, g => g.First().DumpToModel(), StringComparer.Ordinal);

        OpenTK.Mathematics.Vector3? Resolve(string refName)
        {
            if (string.IsNullOrEmpty(refName)) return null;
            if (!keyVertsByName.TryGetValue(refName, out var kv)) return null;
            if (kv == null || string.IsNullOrEmpty(kv.ShapeName)) return null;
            return viewer.TryGetCurrentVertex(kv.ShapeName, kv.VertexIndex, out var p)
                ? (OpenTK.Mathematics.Vector3?)p
                : null;
        }

        var segments = new List<(OpenTK.Mathematics.Vector3 A, OpenTK.Mathematics.Vector3 B, OpenTK.Mathematics.Vector3 Color)>();

        // Yellow for the primary pair, cyan for the ratio denominator pair.
        var primary = new OpenTK.Mathematics.Vector3(1.0f, 0.85f, 0.1f);
        var secondary = new OpenTK.Mathematics.Vector3(0.1f, 0.85f, 1.0f);

        var a = Resolve(sel.VertexRefA);
        var b = Resolve(sel.VertexRefB);
        if (a.HasValue && b.HasValue)
        {
            segments.Add((a.Value, b.Value, primary));
        }

        if (sel.Kind == MeasurementKind.RatioDistance)
        {
            var c = Resolve(sel.VertexRefC);
            var d = Resolve(sel.VertexRefD);
            if (c.HasValue && d.HasValue)
            {
                segments.Add((c.Value, d.Value, secondary));
            }
        }

        if (segments.Count == 0)
        {
            viewer.SetMeasurementLines(null);
            return;
        }

        viewer.SetMeasurementLines(segments);
    }

    /// <summary>
    /// Generates draft <see cref="MeasurementRule"/>s for <see cref="SelectedDescriptorForLabeling"/>
    /// using a simple 1-D split heuristic over <see cref="LabeledExamples"/>. The user reviews
    /// and promotes drafts in the manual rule editor; suggestions are never auto-applied.
    /// </summary>
    private void RunSuggestThresholds()
    {
        var target = SelectedDescriptorForLabeling;
        if (target == null) return;

        // Group labeled examples for the target descriptor.
        var positives = LabeledExamples
            .Where(l => l.Descriptor != null && SameDescriptor(l.Descriptor, target) && l.Polarity == LabelPolarity.Positive)
            .Select(l => l.RecordedValuesByMeasurementName)
            .ToList();
        var negatives = LabeledExamples
            .Where(l => l.Descriptor != null && SameDescriptor(l.Descriptor, target) && l.Polarity == LabelPolarity.Negative)
            .Select(l => l.RecordedValuesByMeasurementName)
            .ToList();

        if (positives.Count == 0)
        {
            _parent.LogMessage("Suggest: need at least one positive example for " + target.Category + ":" + target.Value);
            return;
        }

        // For each measurement that has at least one positive value, compute the median split.
        var measurementNames = positives.SelectMany(d => d.Keys).Concat(negatives.SelectMany(d => d.Keys)).Distinct().ToList();

        var draftGroup = new AndGatedMeasurementGroup();
        int conditionsAdded = 0;

        foreach (var mname in measurementNames)
        {
            var posVals = positives.Where(d => d.ContainsKey(mname)).Select(d => d[mname]).OrderBy(v => v).ToList();
            var negVals = negatives.Where(d => d.ContainsKey(mname)).Select(d => d[mname]).OrderBy(v => v).ToList();
            if (posVals.Count == 0) continue;

            float posMedian = posVals[posVals.Count / 2];

            // If we have negatives, place the threshold halfway between medians; otherwise widen to +/- 10% around the positive median.
            if (negVals.Count > 0)
            {
                float negMedian = negVals[negVals.Count / 2];
                if (Math.Abs(posMedian - negMedian) < 1e-4f) continue; // medians collide -- not a useful split

                float threshold = (posMedian + negMedian) * 0.5f;
                var comp = posMedian > negMedian ? MeasurementComparator.GreaterThanOrEqual : MeasurementComparator.LessThanOrEqual;
                draftGroup.ConditionsANDlogic.Add(new MeasurementCondition
                {
                    MeasurementName = mname,
                    Comparator = comp,
                    Value = (float)Math.Round(threshold, 3),
                });
                conditionsAdded++;
            }
            else
            {
                // No negatives -- bracket around the positive median (band rule -- two conditions ANDed).
                float lo = posMedian * 0.9f;
                float hi = posMedian * 1.1f;
                draftGroup.ConditionsANDlogic.Add(new MeasurementCondition
                {
                    MeasurementName = mname,
                    Comparator = MeasurementComparator.GreaterThanOrEqual,
                    Value = (float)Math.Round(lo, 3),
                });
                draftGroup.ConditionsANDlogic.Add(new MeasurementCondition
                {
                    MeasurementName = mname,
                    Comparator = MeasurementComparator.LessThanOrEqual,
                    Value = (float)Math.Round(hi, 3),
                });
                conditionsAdded += 2;
            }
        }

        if (conditionsAdded == 0)
        {
            _parent.LogMessage("Suggest: no usable measurements found for " + target.Category + ":" + target.Value);
            return;
        }

        var draftRule = new MeasurementRule
        {
            Descriptor = new BodyShapeDescriptor.LabelSignature { Category = target.Category, Value = target.Value },
            IsDraft = true,
        };
        draftRule.GroupsORlogic.Add(draftGroup);
        Rules.Add(new VM_MeasurementRule(draftRule, this));
    }

    private static bool SameDescriptor(BodyShapeDescriptor.LabelSignature a, BodyShapeDescriptor.LabelSignature b)
    {
        return a != null && b != null
            && string.Equals(a.Category, b.Category, StringComparison.Ordinal)
            && string.Equals(a.Value, b.Value, StringComparison.Ordinal);
    }

    private static string NextDefaultName(string prefix, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing.Where(s => !string.IsNullOrEmpty(s)));
        for (int i = 1; i < 1000; i++)
        {
            var candidate = prefix + "_" + i;
            if (!taken.Contains(candidate)) return candidate;
        }
        return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    public BodyTypeProfile DumpToModel()
    {
        var model = new BodyTypeProfile
        {
            Id = Id,
            Name = Name?.Trim() ?? "",
            BodyTypeName = BodyTypeName?.Trim() ?? "",
            Fingerprint = new TopologyFingerprint
            {
                VertexCount = FingerprintVertexCount,
                ShapeVertexCounts = ParseShapeCounts(FingerprintShapeCounts),
                SampleIndices = _source.Fingerprint?.SampleIndices ?? new List<int>(),
            },
            KeyVertices = KeyVertices.Select(k => k.DumpToModel()).ToList(),
            Measurements = Measurements.Select(m => m.DumpToModel()).ToList(),
            Rules = Rules.Select(r => r.DumpToModel()).ToList(),
            LabeledExamples = LabeledExamples.Select(l => l.DumpToModel()).ToList(),
        };
        return model;
    }

    private static Dictionary<string, int> ParseShapeCounts(string csv)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var token in csv.Split(','))
        {
            var parts = token.Split(':');
            if (parts.Length != 2) continue;
            var name = parts[0].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (int.TryParse(parts[1].Trim(), out int count))
            {
                result[name] = count;
            }
        }
        return result;
    }
}

/// <summary>Row VM for a single <see cref="NamedKeyVertex"/>.</summary>
public class VM_NamedKeyVertex : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_NamedKeyVertex(NamedKeyVertex source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        Name = source.Name ?? "";
        ShapeName = source.ShapeName ?? "";
        VertexIndex = source.VertexIndex;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.KeyVertices.Remove(this));
    }

    public string Name { get; set; }
    public string ShapeName { get; set; }
    public int VertexIndex { get; set; }

    public RelayCommand DeleteCommand { get; }

    public NamedKeyVertex DumpToModel() => new()
    {
        Name = Name?.Trim() ?? "",
        ShapeName = ShapeName?.Trim() ?? "",
        VertexIndex = VertexIndex,
    };
}

/// <summary>Row VM for a single <see cref="MeasurementDefinition"/>. Tracks the live readout value.</summary>
public class VM_MeasurementDefinition : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_MeasurementDefinition(MeasurementDefinition source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        Name = source.Name ?? "";
        Kind = source.Kind;
        Axis = source.Axis;

        var refs = source.VertexRefNames ?? new List<string>();
        VertexRefA = refs.Count > 0 ? refs[0] : "";
        VertexRefB = refs.Count > 1 ? refs[1] : "";
        VertexRefC = refs.Count > 2 ? refs[2] : "";
        VertexRefD = refs.Count > 3 ? refs[3] : "";

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.Measurements.Remove(this));
    }

    public string Name { get; set; }
    public MeasurementKind Kind { get; set; }
    public MeasurementAxis Axis { get; set; }

    public string VertexRefA { get; set; }
    public string VertexRefB { get; set; }
    public string VertexRefC { get; set; }
    public string VertexRefD { get; set; }

    /// <summary>Latest evaluated value against the active viewer; null when no viewer or evaluation failed.</summary>
    public float? LiveValue { get; set; }

    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableKeyVertexNames => _parent.AvailableKeyVertexNames;

    public bool ShowAxisField => Kind == MeasurementKind.AxisDistance;
    public bool ShowSecondPair => Kind == MeasurementKind.RatioDistance;

    /// <summary>Display-friendly labels for <see cref="MeasurementAxis"/>. Viewer positions are in
    /// HelixToolkit Y-up space (see BodySlideDeformer remarks), so X=left/right, Y=up/down, Z=front/back.</summary>
    public static IReadOnlyList<AxisOption> AxisOptions { get; } = new[]
    {
        new AxisOption(MeasurementAxis.X, "X (Horizontal)"),
        new AxisOption(MeasurementAxis.Y, "Y (Vertical)"),
        new AxisOption(MeasurementAxis.Z, "Z (Depth)"),
    };

    public IReadOnlyList<AxisOption> AxisOptionsList => AxisOptions;

    public MeasurementDefinition DumpToModel()
    {
        var refs = new List<string>();
        if (!string.IsNullOrEmpty(VertexRefA)) refs.Add(VertexRefA);
        if (!string.IsNullOrEmpty(VertexRefB)) refs.Add(VertexRefB);
        if (Kind == MeasurementKind.RatioDistance)
        {
            if (!string.IsNullOrEmpty(VertexRefC)) refs.Add(VertexRefC);
            if (!string.IsNullOrEmpty(VertexRefD)) refs.Add(VertexRefD);
        }
        return new MeasurementDefinition
        {
            Name = Name?.Trim() ?? "",
            Kind = Kind,
            Axis = Axis,
            VertexRefNames = refs,
        };
    }
}

/// <summary>Paired (value, display-label) for the axis ComboBox in the measurement grid.</summary>
public sealed class AxisOption
{
    public AxisOption(MeasurementAxis value, string label)
    {
        Value = value;
        Label = label;
    }
    public MeasurementAxis Value { get; }
    public string Label { get; }
}

/// <summary>Row VM for a <see cref="MeasurementRule"/>.</summary>
public class VM_MeasurementRule : VM
{
    private readonly VM_BodyTypeProfile _parent;
    private readonly string _id;

    public VM_MeasurementRule(MeasurementRule source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        _id = source.Id ?? Guid.NewGuid().ToString("N");
        DescriptorCategory = source.Descriptor?.Category ?? "";
        DescriptorValue = source.Descriptor?.Value ?? "";
        IsDraft = source.IsDraft;

        if (source.GroupsORlogic != null)
        {
            foreach (var g in source.GroupsORlogic)
            {
                if (g == null) continue;
                Groups.Add(new VM_AndGatedMeasurementGroup(g, this));
            }
        }

        AddGroup = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Groups.Add(new VM_AndGatedMeasurementGroup(new AndGatedMeasurementGroup(), this)));

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.Rules.Remove(this));

        PromoteFromDraft = new RelayCommand(
            canExecute: _ => IsDraft,
            execute: _ => IsDraft = false);
    }

    public string DescriptorCategory { get; set; }
    public string DescriptorValue { get; set; }
    public bool IsDraft { get; set; }

    public ObservableCollection<VM_AndGatedMeasurementGroup> Groups { get; } = new();

    public RelayCommand AddGroup { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand PromoteFromDraft { get; }

    public ObservableCollection<BodyShapeDescriptor.LabelSignature> AvailableDescriptors => _parent.AvailableDescriptors;

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public void RemoveGroup(VM_AndGatedMeasurementGroup group) => Groups.Remove(group);

    public MeasurementRule DumpToModel()
    {
        return new MeasurementRule
        {
            Id = _id,
            Descriptor = new BodyShapeDescriptor.LabelSignature
            {
                Category = DescriptorCategory?.Trim() ?? "",
                Value = DescriptorValue?.Trim() ?? "",
            },
            IsDraft = IsDraft,
            GroupsORlogic = Groups.Select(g => g.DumpToModel()).ToList(),
        };
    }
}

/// <summary>Row VM for an <see cref="AndGatedMeasurementGroup"/>.</summary>
public class VM_AndGatedMeasurementGroup : VM
{
    private readonly VM_MeasurementRule _parent;

    public VM_AndGatedMeasurementGroup(AndGatedMeasurementGroup source, VM_MeasurementRule parent)
    {
        _parent = parent;

        if (source.ConditionsANDlogic != null)
        {
            foreach (var c in source.ConditionsANDlogic)
            {
                if (c == null) continue;
                Conditions.Add(new VM_MeasurementCondition(c, this));
            }
        }

        AddCondition = new RelayCommand(
            canExecute: _ => true,
            execute: _ => Conditions.Add(new VM_MeasurementCondition(new MeasurementCondition(), this)));

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.RemoveGroup(this));
    }

    public ObservableCollection<VM_MeasurementCondition> Conditions { get; } = new();
    public RelayCommand AddCondition { get; }
    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public void RemoveCondition(VM_MeasurementCondition condition) => Conditions.Remove(condition);

    public AndGatedMeasurementGroup DumpToModel() => new()
    {
        ConditionsANDlogic = Conditions.Select(c => c.DumpToModel()).ToList(),
    };
}

/// <summary>Row VM for a <see cref="MeasurementCondition"/>.</summary>
public class VM_MeasurementCondition : VM
{
    private readonly VM_AndGatedMeasurementGroup _parent;

    public VM_MeasurementCondition(MeasurementCondition source, VM_AndGatedMeasurementGroup parent)
    {
        _parent = parent;
        MeasurementName = source.MeasurementName ?? "";
        Comparator = source.Comparator;
        Value = source.Value;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.RemoveCondition(this));
    }

    public string MeasurementName { get; set; }
    public MeasurementComparator Comparator { get; set; }
    public float Value { get; set; }
    public RelayCommand DeleteCommand { get; }

    public IEnumerable<string> AvailableMeasurementNames => _parent.AvailableMeasurementNames;

    public MeasurementCondition DumpToModel() => new()
    {
        MeasurementName = MeasurementName?.Trim() ?? "",
        Comparator = Comparator,
        Value = Value,
    };
}

/// <summary>
/// Row VM for a <see cref="LabeledExample"/>. Snapshots the recorded measurement values so the
/// suggest pass can re-run from persisted data without needing the original viewer state.
/// </summary>
public class VM_LabeledExample : VM
{
    private readonly VM_BodyTypeProfile _parent;

    public VM_LabeledExample(LabeledExample source, VM_BodyTypeProfile parent)
    {
        _parent = parent;
        DescriptorCategory = source.Descriptor?.Category ?? "";
        DescriptorValue = source.Descriptor?.Value ?? "";
        PresetLabel = source.PresetLabel ?? "";
        PresetGender = source.PresetGender;
        Weight = source.Weight;
        Polarity = source.Polarity;

        DeleteCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => _parent.LabeledExamples.Remove(this));
    }

    public string DescriptorCategory { get; set; }
    public string DescriptorValue { get; set; }
    public string PresetLabel { get; set; }
    public Gender PresetGender { get; set; }
    public int Weight { get; set; }
    public LabelPolarity Polarity { get; set; }

    public RelayCommand DeleteCommand { get; }

    /// <summary>
    /// Measurement readings captured at labeling time. Stored runtime-only on the VM and
    /// re-emitted into the persisted <see cref="LabeledExample"/> so the suggest pass can
    /// score across sessions without re-loading the original preset/weight.
    /// </summary>
    public Dictionary<string, float> RecordedValuesByMeasurementName { get; set; } = new();

    public BodyShapeDescriptor.LabelSignature Descriptor => new()
    {
        Category = DescriptorCategory ?? "",
        Value = DescriptorValue ?? "",
    };

    public LabeledExample DumpToModel() => new()
    {
        Descriptor = Descriptor,
        PresetLabel = PresetLabel?.Trim() ?? "",
        PresetGender = PresetGender,
        Weight = Weight,
        Polarity = Polarity,
    };
}

