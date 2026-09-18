using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// Top-section VM for the new Label-then-Suggest tab. Owns the reused
/// <see cref="VM_BodyShapeDescriptorSelectionMenu"/> in annotation mode -- whatever the user
/// checks is interpreted as "this descriptor applies to the currently-selected (preset, weight)
/// slice." Two-way sync with the row's <see cref="VM_PresetAnnotationRow.CurrentDescriptors"/>
/// and the persisted <see cref="PresetAnnotation"/> on the active profile, so:
/// <list type="bullet">
///   <item>Clicking a row in the table refreshes the menu's checks to match that slice's saved
///         annotation (or clears them when the slice has none yet).</item>
///   <item>Toggling a descriptor check writes back into the row's CurrentDescriptors, the
///         profile's PresetAnnotations list, and the row's annotation summary in one round.</item>
/// </list>
/// </summary>
public class VM_PresetAnnotationEditor : VM
{
    private readonly VM_BodyTypeProfileEditor _editor;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _menuFactory;
    private bool _suppressMenuToRowSync;
    private IDisposable _headerSubscription;

    public VM_PresetAnnotationEditor(
        VM_BodyTypeProfileEditor editor,
        VM_BodyShapeDescriptorSelectionMenu.Factory menuFactory)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _menuFactory = menuFactory ?? throw new ArgumentNullException(nameof(menuFactory));

        // The bottom-section table is the source of truth for "which row are we editing".
        _editor.AnnotationTable.PropertyChanged += OnAnnotationTablePropertyChanged;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CurrentRow))
            {
                OnCurrentRowChanged();
            }
        };
    }

    /// <summary>The descriptor-selection menu reused from Match Presets. Constructed lazily by
    /// <see cref="InitializeMenu"/> after <see cref="VM_SettingsOBody.DescriptorUI"/> exists.
    /// Null until then; the XAML degrades gracefully when bound to null content.</summary>
    public VM_BodyShapeDescriptorSelectionMenu DescriptorMenu { get; private set; }

    /// <summary>The (preset, gender, weight) slice currently being annotated. Mirrors
    /// <see cref="VM_PresetAnnotationTable.SelectedRow"/>; setting it programmatically (e.g. from
    /// Phase 5/6 panels) also moves the table selection so the viewer follows along.</summary>
    public VM_PresetAnnotationRow CurrentRow { get; set; }

    /// <summary>Display-friendly title for the editor pane. Switches between
    /// "Annotating: {preset @ weight}" and a "Select a preset row..." placeholder.</summary>
    public string Title { get; private set; } = "Select a preset row in the table below to start annotating.";

    /// <summary>True when the descriptor menu is wired up (post-InitializeMenu) and a row is
    /// selected. Bound to the menu pane's IsEnabled in XAML; updated by
    /// <see cref="OnCurrentRowChanged"/> so Fody's PropertyChanged refreshes the binding.</summary>
    public bool IsEditingEnabled { get; private set; }

    /// <summary>Two-phase init. Constructs the <see cref="DescriptorMenu"/> in annotation mode
    /// (no match-mode dropdown, no priority numbers) and subscribes to its <c>Header</c>
    /// property -- the cheap catch-all for "user toggled something in the menu."</summary>
    public void InitializeMenu(VM_SettingsOBody oBodyVM, ObservableCollection<VM_RaceGrouping> raceGroupingVMs)
    {
        if (DescriptorMenu != null || _menuFactory == null || oBodyVM == null) return;

        DescriptorMenu = _menuFactory(
            oBodyVM.DescriptorUI,
            raceGroupingVMs,
            oBodyVM,
            // showMatchMode = false: annotation has no All/Any/Shared semantics; the user is
            // assigning a fixed set of descriptors to the slice, not querying.
            false,
            DescriptorMatchMode.All,
            // showPriority = false: simple binary annotation; priority is ClassifierRules-only.
            false);

        // Annotation is a judgment about a body, not a distribution choice, so the rules-only
        // categories the distribution pickers hide belong here: a rule gated on ShoulderWidth:Wide
        // can only be fitted if the user can record that verdict in the first place.
        DescriptorMenu.SetIncludeRulesOnly(true);

        _headerSubscription = this.WhenAnyValue(x => x.DescriptorMenu.Header)
            .Skip(1)
            .Subscribe(_ => OnDescriptorMenuChanged());

        // Kick the title / IsEditingEnabled to refresh now that the menu exists.
        OnCurrentRowChanged();
    }

    public override void Dispose()
    {
        _headerSubscription?.Dispose();
        base.Dispose();
    }

    // ---------- annotation-queue support ----------
    //
    // The queue drives this editor rather than duplicating it: it moves the table selection (which
    // this VM already mirrors into CurrentRow) and toggles values through the helpers below, so
    // every write still goes out through OnDescriptorMenuChanged's single persistence path. There
    // is deliberately no second writer to profile.PresetAnnotations.

    /// <summary>Descriptor categories the menu can actually toggle, in menu order. The queue offers
    /// exactly these as target categories -- a category the editor cannot express a verdict in is
    /// not one the queue should be serving slices for.</summary>
    public IReadOnlyList<string> GetCategories()
    {
        var result = new List<string>();
        if (DescriptorMenu == null) return result;
        foreach (var shell in DescriptorMenu.DescriptorShells)
        {
            var category = shell?.TrackedShell?.Category;
            if (!string.IsNullOrEmpty(category)) result.Add(category);
        }
        return result;
    }

    /// <summary>Values available in <paramref name="category"/>, in menu order. The queue shows
    /// these numbered so the user can see which digit key toggles which value.</summary>
    public IReadOnlyList<string> GetCategoryValues(string category)
    {
        var result = new List<string>();
        var shell = FindShell(category);
        if (shell == null) return result;
        foreach (var selector in shell.DescriptorSelectors)
        {
            if (selector != null && !string.IsNullOrEmpty(selector.Value)) result.Add(selector.Value);
        }
        return result;
    }

    /// <summary>
    /// Flips the selected state of the <paramref name="oneBasedIndex"/>-th value of
    /// <paramref name="category"/> -- the digit-key path.
    /// <para>Toggle, not select: descriptor categories are tag sets. Decision <c>D23</c> settled
    /// that <c>Belly = Fat + Pregnant</c> is a legitimate dual assignment and that
    /// <c>Nero Preggo</c> genuinely carries both, so a single-select control would make correct
    /// labels unrepresentable.</para>
    /// </summary>
    /// <returns>The value that was toggled, or null when the index is out of range. The caller
    /// uses the return to decide whether the keystroke did anything worth reporting.</returns>
    public string ToggleValueByIndex(string category, int oneBasedIndex)
    {
        var shell = FindShell(category);
        if (shell == null) return null;
        int i = oneBasedIndex - 1;
        if (i < 0 || i >= shell.DescriptorSelectors.Count) return null;
        var selector = shell.DescriptorSelectors[i];
        if (selector == null) return null;
        // Writing IsSelected fires the menu's Header subscription, which routes through
        // OnDescriptorMenuChanged and persists -- same path a mouse click takes.
        selector.IsSelected = !selector.IsSelected;
        return selector.Value;
    }

    /// <summary>Values currently selected in <paramref name="category"/> for the active row. The
    /// queue reads this on commit to propagate the verdict to alias slices and to tally it.</summary>
    public IReadOnlyList<string> GetSelectedValues(string category)
    {
        var result = new List<string>();
        var shell = FindShell(category);
        if (shell == null) return result;
        foreach (var selector in shell.DescriptorSelectors)
        {
            if (selector != null && selector.IsSelected && !string.IsNullOrEmpty(selector.Value))
            {
                result.Add(selector.Value);
            }
        }
        return result;
    }

    private VM_BodyShapeDescriptorShellSelector FindShell(string category)
    {
        if (DescriptorMenu == null || string.IsNullOrEmpty(category)) return null;
        foreach (var shell in DescriptorMenu.DescriptorShells)
        {
            if (shell?.TrackedShell == null) continue;
            if (string.Equals(shell.TrackedShell.Category, category, StringComparison.Ordinal)) return shell;
        }
        return null;
    }

    private void OnAnnotationTablePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VM_PresetAnnotationTable.SelectedRow))
        {
            CurrentRow = _editor.AnnotationTable.SelectedRow;
        }
    }

    private void OnCurrentRowChanged()
    {
        Title = CurrentRow == null
            ? "Select a preset row in the table below to start annotating."
            : $"Annotating: {CurrentRow.PresetLabel} @ weight {CurrentRow.Weight} ({CurrentRow.Gender})";
        IsEditingEnabled = DescriptorMenu != null && CurrentRow != null;

        if (DescriptorMenu == null) return;

        _suppressMenuToRowSync = true;
        try
        {
            // Reset the menu by clearing the slate, then re-applying whatever the row says.
            // DeselectAll only clears the UI selectors; BackupStash holds descriptors that
            // were on the previous row but absent from the template UI, and DumpToHashSet
            // unions BackupStash into its output -- so we MUST also clear BackupStash here
            // or orphan descriptors from the previously-edited row would leak into the new
            // row's saved annotation on the next round-trip.
            DescriptorMenu.DeselectAll();
            DescriptorMenu.BackupStash.Clear();
            if (CurrentRow == null) return;

            var existing = new HashSet<BodyShapeDescriptor.LabelSignature>(LabelSignatureKeyComparer.Instance);
            foreach (var d in CurrentRow.CurrentDescriptors)
            {
                if (d == null) continue;
                existing.Add(new BodyShapeDescriptor.LabelSignature
                {
                    Category = d.Category ?? "",
                    Value = d.Value ?? "",
                });
            }
            DescriptorMenu.CopyInFromHashSet(existing);
        }
        finally
        {
            _suppressMenuToRowSync = false;
        }
    }

    private void OnDescriptorMenuChanged()
    {
        // CopyInFromHashSet inside OnCurrentRowChanged moves the menu's checks; the Header
        // subscription would round-trip those changes back into CurrentRow and re-fire ad
        // infinitum. _suppressMenuToRowSync guards against that initialization storm.
        if (_suppressMenuToRowSync) return;
        var row = CurrentRow;
        if (row == null || DescriptorMenu == null) return;

        // Snapshot the menu's selection.
        var selected = DescriptorMenu.DumpToHashSet();

        // Sync the row's CurrentDescriptors first -- the table summary refreshes off this.
        row.CurrentDescriptors.Clear();
        foreach (var d in selected)
        {
            if (d == null) continue;
            row.CurrentDescriptors.Add(new BodyShapeDescriptor.LabelSignature
            {
                Category = d.Category ?? "",
                Value = d.Value ?? "",
            });
        }

        // Then mirror into the persisted PresetAnnotations on the active profile so the choice
        // survives session restart. Prune the entry entirely when the user deselects everything
        // -- empty-list annotations would clutter the saved profile with noise.
        var profile = _editor.SelectedProfile;
        if (profile == null) return;
        var existing = profile.FindAnnotation(row.PresetLabel, row.Gender, row.Weight);
        if (selected.Count == 0)
        {
            if (existing != null) profile.PresetAnnotations.Remove(existing);
            return;
        }
        if (existing == null)
        {
            existing = new PresetAnnotation
            {
                PresetLabel = row.PresetLabel,
                PresetGender = row.Gender,
                Weight = row.Weight,
            };
            profile.PresetAnnotations.Add(existing);
        }
        existing.Descriptors.Clear();
        foreach (var d in selected)
        {
            if (d == null) continue;
            existing.Descriptors.Add(new BodyShapeDescriptor.LabelSignature
            {
                Category = d.Category ?? "",
                Value = d.Value ?? "",
            });
        }
    }
}

/// <summary>Hash / equality on the (Category, Value) pair only. <c>LabelSignature</c> defaults
/// to reference equality, which would defeat the HashSet&lt;&gt; we feed
/// <see cref="VM_BodyShapeDescriptorSelectionMenu.CopyInFromHashSet{T}"/>.</summary>
internal sealed class LabelSignatureKeyComparer : IEqualityComparer<BodyShapeDescriptor.LabelSignature>
{
    public static readonly LabelSignatureKeyComparer Instance = new();

    public bool Equals(BodyShapeDescriptor.LabelSignature x, BodyShapeDescriptor.LabelSignature y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return string.Equals(x.Category ?? "", y.Category ?? "", StringComparison.Ordinal)
            && string.Equals(x.Value ?? "", y.Value ?? "", StringComparison.Ordinal);
    }

    public int GetHashCode(BodyShapeDescriptor.LabelSignature obj)
    {
        if (obj == null) return 0;
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (obj.Category ?? "").GetHashCode(StringComparison.Ordinal);
            hash = hash * 31 + (obj.Value ?? "").GetHashCode(StringComparison.Ordinal);
            return hash;
        }
    }
}
