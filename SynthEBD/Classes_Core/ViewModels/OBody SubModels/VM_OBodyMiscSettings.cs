using Mutagen.Bethesda.Fallout4;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;

namespace SynthEBD;

public class VM_OBodyMiscSettings : VM
{
    private readonly Logger _logger;
    private readonly RaceMenuIniHandler _raceMenuHandler;
    private readonly VM_Settings_General _generalSettingsVM;
    private readonly Func<VM_SettingsOBody> _parentMenu;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;

    /// <summary>True while rows are being (re)built, so the SelectedFile setters don't trigger file switches.</summary>
    private bool _suppressRuleFileSwitch;
    public delegate VM_OBodyMiscSettings Factory();
    public VM_OBodyMiscSettings(Logger logger, RaceMenuIniHandler raceMenuHandler, VM_Settings_General generalSettingsVM, Func<VM_SettingsOBody> parentMenu, VM_OBodyPreviewNpcSettings previewNpcs, DescriptorDefaultSynchronizer descriptorDefaultSynchronizer, PatcherState patcherState, SynthEBDPaths paths)
    {
        _logger = logger;
        _raceMenuHandler = raceMenuHandler;
        _generalSettingsVM = generalSettingsVM;
        _parentMenu = parentMenu;
        _patcherState = patcherState;
        _paths = paths;
        PreviewNpcs = previewNpcs;

        OpenRuleFolderCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => WinExplorerOpener.OpenFolder(_paths.BodyTypeRulesDirPath));
        // The synchronizer reads PreferSliderDefaultsOnConflict live from this VM at each
        // reconcile, so mid-session toggle changes apply to the next import/retarget without
        // waiting for a save round-trip.
        descriptorDefaultSynchronizer.RegisterMiscSettings(this);

        generalSettingsVM.WhenAnyValue(x => x.BSSelectionMode).Subscribe(mode => {
            
            switch(mode)
            {
                case BodySlideSelectionMode.OBody:
                    ShowOBodySelectionMode = true;
                    ShowAutoBodySelectionMode = false;
                    break;
                case BodySlideSelectionMode.AutoBody:
                    ShowAutoBodySelectionMode = true;
                    ShowOBodySelectionMode = false;
                    break;
            }
        }).DisposeWith(this);

        UiModeController.Instance.WhenAnyValue(x => x.DisplayMode).Subscribe(x => bShowTroubleshootingSettings = x == UiDisplayMode.Troubleshoot).DisposeWith(this);

        this.WhenAnyValue(x => x.OBodySelectionMode).Subscribe(mode => ShowOBodyNativeOptions = mode == OBodySelectionMode.Native).DisposeWith(this);

        // Mismatch popup: fire once, the first time this menu is actually displayed.
        // We can't subscribe synchronously here because the Autofac resolution chain is
        //   VM_SettingsOBody .ctor  →  miscSettingsFactory()  →  this .ctor
        // and Observable.Defer + Subscribe still invokes the factory eagerly at Subscribe
        // time, which would re-enter Autofac for VM_SettingsOBody while it is mid-construction
        // and trip CircularDependencyDetectorMiddleware. BeginInvoke hops off the current
        // stack so parentMenu() only runs after VM_SettingsOBody has finished constructing.
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            parentMenu().WhenAnyValue(x => x.DisplayedUI)
                .Skip(1)
                .Where(displayed => ReferenceEquals(displayed, this))
                .Take(1)
                .Subscribe(_ => ShowPreviewMismatchPopupIfNeeded())
                .DisposeWith(this);
        }));

        AddSliderCatalogOverride = new RelayCommand(
            canExecute: _ => true,
            execute: _ => SliderCatalogOverrides.Add(new VM_SliderCatalogOverride("", "", SliderCatalogOverrides))
        );

        AddBodyTypeFamily = new RelayCommand(
            canExecute: _ => true,
            execute: _ => BodyTypeFamilies.Add(new VM_BodyTypeFamily("", "", BodyTypeFamilies))
        );

        SetRaceMenuINI = new(
            canExecute: _ => true,
            execute: _ =>
            {
                if (_raceMenuHandler.SetRaceMenuIniForBodySlide())
                {
                    _logger.CallTimedLogErrorWithStatusUpdateAsync("RaceMenu Ini set successfully", ErrorType.Warning, 2); // Warning yellow font is easier to see than green
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Error encountered trying to set RaceMenu's ini.", ErrorType.Error);
                }
            }
        );

        RemoveStashedDescriptors = new(
            canExecute: _ => true,
            execute: _ =>
            {
                var toRemove = StashedDescriptors.Where(x => x.IsSelected).Select(x => x.Text).ToArray();

                var parentMenu = _parentMenu();

                foreach (var bs in parentMenu.BodySlidesUI.BodySlidesMale.And(parentMenu.BodySlidesUI.BodySlidesFemale).ToArray())
                {
                    bs.AssociatedModel.RemoveDescriptorsFromAllSlots(d => toRemove.Contains(d.ToLabelSignature().ToString()));
                }

                StashedDescriptors.RemoveWhere(x => x.IsSelected);

                ShowRemoveStashedDescriptorsButton = StashedDescriptors.Any();
            }
        );
    }

    public bool UseVerboseScripts { get; set; } = false;
    public AutoBodySelectionMode AutoBodySelectionMode { get; set; } = AutoBodySelectionMode.INI;
    public OBodySelectionMode OBodySelectionMode { get; set; } = OBodySelectionMode.Native;
    public RelayCommand SetRaceMenuINI { get; set; }
    public bool OBodyEnableMultipleAssignments { get; set; } = false;
    public bool ShowOBodyNativeOptions { get; set; } = false;
    public bool ShowAutoBodySelectionMode { get; set; }
    public bool ShowOBodySelectionMode { get; set; }
    public bool AutoApplyMissingAnnotations { get; set; } = true;

    /// <summary>Mirror of <see cref="Settings_OBody.PreferSliderDefaultsOnConflict"/>: which side
    /// wins when load-time reconciliation of the shared per-category default descriptor finds the
    /// Label by Sliders and Label by Measurements menus disagreeing. False (default) = the
    /// measurement-profile value wins. Read live by <see cref="DescriptorDefaultSynchronizer"/>.</summary>
    public bool PreferSliderDefaultsOnConflict { get; set; } = false;

    public bool bShowTroubleshootingSettings { get; set; } = false;
    public ObservableCollection<VM_SelectableMenuString> StashedDescriptors { get; set; } = new();
    public RelayCommand RemoveStashedDescriptors { get; }
    public bool ShowRemoveStashedDescriptorsButton { get; set; } = false;

    // Stage 4: slider catalog overrides + body type family compatibility
    public ObservableCollection<VM_SliderCatalogOverride> SliderCatalogOverrides { get; set; } = new();
    public RelayCommand AddSliderCatalogOverride { get; }
    public ObservableCollection<VM_BodyTypeFamily> BodyTypeFamilies { get; set; } = new();
    public RelayCommand AddBodyTypeFamily { get; }

    /// <summary>
    /// Section B: per-weight preview NPC mapping consumed by the BodySlide preview viewer.
    /// </summary>
    public VM_OBodyPreviewNpcSettings PreviewNpcs { get; }

    /// <summary>One row per body type that has at least one Body Type Rules file: a combobox choosing the
    /// active file, which drives both Label by Sliders and Label by Measurements for that body type.</summary>
    public ObservableCollection<VM_BodyTypeRuleFileSelection> RuleFileSelections { get; } = new();

    /// <summary>Opens the Body Type Rules folder in Explorer (where shared rule files are dropped in).</summary>
    public RelayCommand OpenRuleFolderCommand { get; }

    /// <summary>Rebuilds <see cref="RuleFileSelections"/> from <see cref="PatcherState.BodyTypeRuleSets"/>,
    /// selecting each body type's active file per <paramref name="model"/>. Never triggers a switch.</summary>
    public void RebuildRuleFileSelections(Settings_OBody model)
    {
        _suppressRuleFileSwitch = true;
        try
        {
            RuleFileSelections.Clear();
            var ruleSets = _patcherState.BodyTypeRuleSets ?? new List<BodyTypeRuleSet>();
            foreach (var bodyType in SettingsIO_BodyTypeRules.BodyTypesWithFiles(ruleSets).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var row = new VM_BodyTypeRuleFileSelection(bodyType, this);
                foreach (var file in SettingsIO_BodyTypeRules.FilesForBodyType(ruleSets, bodyType))
                {
                    row.Files.Add(new VM_BodyTypeRuleFileOption(file));
                }
                var active = SettingsIO_BodyTypeRules.ResolveActive(model, ruleSets, bodyType);
                row.SelectedFile = row.Files.FirstOrDefault(f => ReferenceEquals(f.Model, active));
                RuleFileSelections.Add(row);
            }
        }
        finally
        {
            _suppressRuleFileSwitch = false;
        }
    }

    /// <summary>The rule file currently active for <paramref name="bodyType"/>, or null when it has none.</summary>
    public BodyTypeRuleSet? GetActiveRuleFile(string bodyType)
        => RuleFileSelections.FirstOrDefault(r => string.Equals(r.BodyTypeName, bodyType, StringComparison.OrdinalIgnoreCase))?.SelectedFile?.Model;

    /// <summary>Makes sure <paramref name="bodyType"/> has an active rule file, creating an empty
    /// (not yet saved) one named after the body type if needed. Returns the active file.</summary>
    public BodyTypeRuleSet EnsureRuleFileForBodyType(string bodyType)
    {
        var existing = GetActiveRuleFile(bodyType);
        if (existing != null) return existing;

        var ruleSets = _patcherState.BodyTypeRuleSets ??= new List<BodyTypeRuleSet>();
        var created = new BodyTypeRuleSet
        {
            Name = bodyType,
            BodyTypeName = bodyType,
            FilePath = SettingsIO_BodyTypeRules.UniqueFilePath(_paths.BodyTypeRulesDirPath, bodyType, ruleSets),
        };
        created.NormalizeBodyType();
        ruleSets.Add(created);
        AddFileToRow(created, select: true);
        _logger.LogMessage("Body Type Rules: created rule file '" + created.FileName + "' for body type '" + bodyType + "' (written on next save).");
        return created;
    }

    /// <summary>Duplicates <paramref name="row"/>'s active file -- including unsaved edits in the labeling
    /// menus -- and makes the copy active, so the user can start modifying a variant right away.</summary>
    internal void DuplicateActiveRuleFile(VM_BodyTypeRuleFileSelection row)
    {
        var source = row?.SelectedFile?.Model;
        if (row == null || source == null) return;
        _parentMenu().FlushLabelingStateIntoRuleFile(row.BodyTypeName, source);
        var copy = SettingsIO_BodyTypeRules.Duplicate(source, _paths.BodyTypeRulesDirPath, _patcherState.BodyTypeRuleSets);
        _patcherState.BodyTypeRuleSets.Add(copy);
        // The copy's content equals the live menus' content, so selecting it needs no reload.
        AddFileToRow(copy, select: true);
        _logger.LogMessage("Body Type Rules: duplicated '" + source.Name + "' as '" + copy.Name + "' (" + copy.FileName + ", written on next save).");
    }

    private void AddFileToRow(BodyTypeRuleSet file, bool select)
    {
        _suppressRuleFileSwitch = true;
        try
        {
            var row = RuleFileSelections.FirstOrDefault(r => string.Equals(r.BodyTypeName, file.BodyTypeName, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new VM_BodyTypeRuleFileSelection(file.BodyTypeName, this);
                RuleFileSelections.Add(row);
            }
            var option = new VM_BodyTypeRuleFileOption(file);
            row.Files.Add(option);
            if (select) row.SelectedFile = option;
        }
        finally
        {
            _suppressRuleFileSwitch = false;
        }
        _parentMenu().BodyTypeProfileEditorUI?.RefreshActiveRuleFileLabel();
    }

    /// <summary>Called by a row when the user picks a different file.</summary>
    internal void HandleRuleFileSelectionChanged(VM_BodyTypeRuleFileSelection row, VM_BodyTypeRuleFileOption? previous, VM_BodyTypeRuleFileOption? current)
    {
        if (_suppressRuleFileSwitch || current == null || ReferenceEquals(previous, current)) return;
        _parentMenu().SwitchActiveRuleFile(row.BodyTypeName, previous?.Model, current.Model);
    }

    public void CopyInViewModelFromModel(Settings_OBody model)
    {
        UseVerboseScripts = model.bUseVerboseScripts;
        AutoBodySelectionMode = model.AutoBodySelectionMode;
        AutoApplyMissingAnnotations = model.AutoApplyMissingAnnotations;
        PreferSliderDefaultsOnConflict = model.PreferSliderDefaultsOnConflict;
        OBodySelectionMode = model.OBodySelectionMode;
        OBodyEnableMultipleAssignments = model.OBodyEnableMultipleAssignments;

        var parentMenu = _parentMenu();

        var uiDescriptors = parentMenu.DescriptorUI.TemplateDescriptors.SelectMany(x => x.Descriptors).Select(y => y.Signature).ToArray();

        HashSet<string> stashedDescriptorSignatures = new();

        foreach (var bs in parentMenu.BodySlidesUI.BodySlidesMale.And(parentMenu.BodySlidesUI.BodySlidesFemale))
        {
            foreach (var descriptor in bs.AssociatedModel.EnumerateAllDescriptors())
            {
                var str = descriptor.ToLabelSignature().ToString();
                if (!uiDescriptors.Contains(str) && !stashedDescriptorSignatures.Contains(str))
                {
                    stashedDescriptorSignatures.Add(str);
                }
            }
        }

        StashedDescriptors.Clear();
        foreach (var str in stashedDescriptorSignatures)
        {
            StashedDescriptors.Add(new() { Text = str, IsSelected = true });
        }

        ShowRemoveStashedDescriptorsButton = StashedDescriptors.Any();

        SliderCatalogOverrides.Clear();
        if (model.SliderCatalogOverridePaths != null)
        {
            foreach (var kv in model.SliderCatalogOverridePaths)
            {
                SliderCatalogOverrides.Add(new VM_SliderCatalogOverride(kv.Key, kv.Value, SliderCatalogOverrides));
            }
        }

        BodyTypeFamilies.Clear();
        if (model.BodyTypeFamilyCompatibility != null)
        {
            foreach (var kv in model.BodyTypeFamilyCompatibility)
            {
                var aliases = kv.Value != null ? string.Join(", ", kv.Value) : string.Empty;
                BodyTypeFamilies.Add(new VM_BodyTypeFamily(kv.Key, aliases, BodyTypeFamilies));
            }
        }

        // Section B: preview-NPC mapping. Walk the *model* preset lists so we don't depend on
        // VM load order (the BodySlide VMs may not exist yet when settings are first applied).
        PreviewNpcs.CopyInFromModel(model.PreviewNpcs, model.BodySlidesMale, model.BodySlidesFemale);
    }

    /// <summary>
    /// One-shot popup that surfaces preview NPCs whose stored weight has drifted outside
    /// tolerance. Drains <see cref="VM_OBodyPreviewNpcSettings.PendingMismatches"/>; safe
    /// to call multiple times (no-op when the list is empty).
    /// </summary>
    public void ShowPreviewMismatchPopupIfNeeded()
    {
        if (PreviewNpcs.PendingMismatches.Count == 0) return;
        var snapshot = PreviewNpcs.PendingMismatches.ToList();
        PreviewNpcs.PendingMismatches.Clear();

        var lines = snapshot.Select(m =>
        {
            var actual = m.ActualWeight.HasValue
                ? m.ActualWeight.Value.ToString("0.0")
                : "(unresolvable)";
            return $"  Weight {m.Weight} ({m.Gender}): saved NPC {m.SavedNpc} now reports weight {actual}";
        });
        var msg =
            "The following BodySlide preview NPCs no longer match their weight slot " +
            "(likely a mod added or changed their weight):\n\n" +
            string.Join("\n", lines) +
            "\n\nAuto-reassign them to the first installed NPC at the right weight, " +
            "or keep the existing assignments?";

        bool reassign = MessageWindow.DisplayNotificationYesNo("BodySlide Preview NPCs Drifted", msg);
        if (reassign)
        {
            PreviewNpcs.AutoReassign(snapshot);
        }
    }

    public void DumpViewModelToModel(Settings_OBody model)
    {
        model.bUseVerboseScripts = UseVerboseScripts;
        model.AutoBodySelectionMode = AutoBodySelectionMode;
        model.AutoApplyMissingAnnotations = AutoApplyMissingAnnotations;
        model.PreferSliderDefaultsOnConflict = PreferSliderDefaultsOnConflict;
        model.OBodySelectionMode = OBodySelectionMode;
        model.OBodyEnableMultipleAssignments = OBodyEnableMultipleAssignments;

        model.SliderCatalogOverridePaths = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var entry in SliderCatalogOverrides)
        {
            var bt = entry.BodyType?.Trim();
            if (string.IsNullOrEmpty(bt)) continue;
            model.SliderCatalogOverridePaths[bt] = entry.Path?.Trim() ?? string.Empty;
        }

        model.BodyTypeFamilyCompatibility = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
        foreach (var entry in BodyTypeFamilies)
        {
            var bt = entry.CanonicalBodyType?.Trim();
            if (string.IsNullOrEmpty(bt)) continue;
            var aliases = (entry.AliasesCsv ?? string.Empty)
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));
            model.BodyTypeFamilyCompatibility[bt] = new System.Collections.Generic.HashSet<string>(aliases);
        }

        model.PreviewNpcs = PreviewNpcs.DumpToModel();

        model.SelectedRuleFileByBodyType = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in RuleFileSelections)
        {
            var fileName = row.SelectedFile?.Model?.FileName;
            if (!string.IsNullOrEmpty(fileName)) model.SelectedRuleFileByBodyType[row.BodyTypeName] = fileName;
        }
    }

    public List<string> ResetTroubleShootingToDefault(bool preparationMode)
    {
        var changes = new List<string>();

        if (UseVerboseScripts)
        {
            if (preparationMode)
            {
                changes.Add("Use Verbose Scripts: True --> False");
            }
            else
            {
                UseVerboseScripts = false;
            }
        }

        return changes;
    }
}

/// <summary>
/// Row of the Misc menu's "Body Type Rules" list: the rule files available for one body type and which
/// one is active. Changing <see cref="SelectedFile"/> switches both labeling menus to that file.
/// </summary>
public class VM_BodyTypeRuleFileSelection : VM
{
    private readonly VM_OBodyMiscSettings _parent;
    private VM_BodyTypeRuleFileOption? _selectedFile;

    public VM_BodyTypeRuleFileSelection(string bodyTypeName, VM_OBodyMiscSettings parent)
    {
        BodyTypeName = bodyTypeName;
        _parent = parent;
        DuplicateCommand = new RelayCommand(
            canExecute: _ => SelectedFile != null,
            execute: _ => _parent.DuplicateActiveRuleFile(this));
    }

    public string BodyTypeName { get; }
    public ObservableCollection<VM_BodyTypeRuleFileOption> Files { get; } = new();

    /// <summary>The active file. Hand-written (not a Fody auto-property) because the switch needs the
    /// previous value, to flush unsaved menu edits into the file being left.</summary>
    public VM_BodyTypeRuleFileOption? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (ReferenceEquals(_selectedFile, value)) return;
            var previous = _selectedFile;
            _selectedFile = value;
            ManuallyRaisePropertyChanged(nameof(SelectedFile));
            _parent.HandleRuleFileSelectionChanged(this, previous, value);
        }
    }

    /// <summary>Copies the active file (with unsaved edits) and makes the copy active.</summary>
    public RelayCommand DuplicateCommand { get; }
}

/// <summary>One file in a <see cref="VM_BodyTypeRuleFileSelection"/>'s combobox. <see cref="Name"/> edits
/// the file's display name in place (the file itself keeps its name on disk).</summary>
public class VM_BodyTypeRuleFileOption : VM
{
    public VM_BodyTypeRuleFileOption(BodyTypeRuleSet model)
    {
        Model = model;
    }

    public BodyTypeRuleSet Model { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (string.Equals(Model.Name, value, StringComparison.Ordinal)) return;
            Model.Name = value ?? "";
            ManuallyRaisePropertyChanged(nameof(Name));
            ManuallyRaisePropertyChanged(nameof(Display));
        }
    }

    /// <summary>"Name (file.json)" -- the file name is shown too so two files with the same display name
    /// can still be told apart.</summary>
    public string Display => Model.Name + "  (" + (string.IsNullOrEmpty(Model.FileName) ? "unsaved" : Model.FileName) + ")";
}

/// <summary>
/// Stage 4: row VM for the Slider Catalog Override list. Each row maps a body type name to a
/// SliderCategories.xml path on disk that the user wants <see cref="SliderCatalogLoader"/> to use
/// instead of the shipped fallback JSON.
/// </summary>
public class VM_SliderCatalogOverride : VM
{
    public VM_SliderCatalogOverride(string bodyType, string path, ObservableCollection<VM_SliderCatalogOverride> parent)
    {
        BodyType = bodyType;
        Path = path;
        ParentCollection = parent;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parent.Remove(this));
        BrowseCommand = new RelayCommand(canExecute: _ => true, execute: _ =>
        {
            var dlg = LongPathHandler.CreateLongPathOpenFileDialog();
            dlg.Filter = "SliderCategories XML (*.xml)|*.xml|All files (*.*)|*.*";
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(Path);
            }
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Path = dlg.FileName;
            }
        });
    }

    public string BodyType { get; set; }
    public string Path { get; set; }
    public ObservableCollection<VM_SliderCatalogOverride> ParentCollection { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand BrowseCommand { get; }
}

/// <summary>
/// Stage 4: row VM for the Body Type Family compatibility list. Maps a canonical body type to a
/// comma-separated list of aliases that <see cref="BodySlideGroupClassifier"/> should collapse onto it.
/// </summary>
public class VM_BodyTypeFamily : VM
{
    public VM_BodyTypeFamily(string canonicalBodyType, string aliasesCsv, ObservableCollection<VM_BodyTypeFamily> parent)
    {
        CanonicalBodyType = canonicalBodyType;
        AliasesCsv = aliasesCsv;
        ParentCollection = parent;
        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parent.Remove(this));
    }

    public string CanonicalBodyType { get; set; }
    public string AliasesCsv { get; set; }
    public ObservableCollection<VM_BodyTypeFamily> ParentCollection { get; }
    public RelayCommand DeleteCommand { get; }
}