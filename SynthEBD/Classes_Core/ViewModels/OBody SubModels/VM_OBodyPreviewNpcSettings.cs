using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// Per-weight preview NPC mapping for the BodySlide preview viewer (Section B).
/// Hosted under VM_OBodyMiscSettings; serialized into Settings_OBody.PreviewNpcs.
///
/// Auto-populates each (weight, gender) row by asking PreviewNpcResolver for the
/// first NPC whose NPC.Weight is within ±5 of the target slot. CopyInFromModel
/// also collects any saved NPCs whose weight has since drifted outside that
/// tolerance — VM_OBodyMiscSettings reads <see cref="PendingMismatches"/> on
/// startup and surfaces a Yes/No popup to auto-reassign them.
/// </summary>
public class VM_OBodyPreviewNpcSettings : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PreviewNpcResolver _previewNpcResolver;
    private readonly Logger _logger;

    public VM_OBodyPreviewNpcSettings(
        IEnvironmentStateProvider environmentProvider,
        PreviewNpcResolver previewNpcResolver,
        Logger logger)
    {
        _environmentProvider = environmentProvider;
        _previewNpcResolver = previewNpcResolver;
        _logger = logger;

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        ResetAllCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => ResetAllRows()
        );
    }

    /// <summary>One row per integer weight slot present in any BodySlide preset.</summary>
    public ObservableCollection<VM_OBodyPreviewWeightRow> Rows { get; } = new();

    public ILinkCache lk { get; private set; }
    public IEnumerable<System.Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    public RelayCommand ResetAllCommand { get; }

    /// <summary>
    /// Populated by CopyInFromModel: weight slots whose stored NPC drifted outside the
    /// ±5 weight tolerance since last save. VM_OBodyMiscSettings drains this list on
    /// the first display of the OBody menu and prompts the user to auto-reassign.
    /// </summary>
    public List<MismatchedPreviewEntry> PendingMismatches { get; } = new();

    /// <summary>
    /// Build rows by unioning weight slots across both gender preset lists, merging
    /// any saved per-weight NPCs, and auto-resolving anything still empty. Re-verifies
    /// each saved NPC's current weight; if it drifted outside ±tolerance, the entry is
    /// recorded in <see cref="PendingMismatches"/> for the popup but not auto-replaced
    /// here (the user picks: keep or reassign).
    /// </summary>
    public void CopyInFromModel(
        OBodyPreviewNpcSettings model,
        IEnumerable<BodySlideSetting> maleBodySlides,
        IEnumerable<BodySlideSetting> femaleBodySlides,
        int tolerance = 5)
    {
        Rows.Clear();
        PendingMismatches.Clear();

        if (model == null) model = new OBodyPreviewNpcSettings();

        var maleWeights = CollectWeightSlots(maleBodySlides);
        var femaleWeights = CollectWeightSlots(femaleBodySlides);
        var allWeights = new SortedSet<int>(maleWeights);
        foreach (var w in femaleWeights) allWeights.Add(w);

        foreach (var weight in allWeights)
        {
            var row = new VM_OBodyPreviewWeightRow(weight, this);

            model.WeightPreviewNpcs.TryGetValue(weight, out var savedPair);
            bool maleApplicableForSlot = maleWeights.Contains(weight);
            bool femaleApplicableForSlot = femaleWeights.Contains(weight);
            row.MaleApplicable = maleApplicableForSlot;
            row.FemaleApplicable = femaleApplicableForSlot;

            if (maleApplicableForSlot)
            {
                row.MaleNpc = ResolveOrAuto(savedPair?.MaleNpc ?? FormKey.Null, Gender.Male, weight, tolerance, isExplicitSave: savedPair != null && !savedPair.MaleNpc.IsNull);
            }
            if (femaleApplicableForSlot)
            {
                row.FemaleNpc = ResolveOrAuto(savedPair?.FemaleNpc ?? FormKey.Null, Gender.Female, weight, tolerance, isExplicitSave: savedPair != null && !savedPair.FemaleNpc.IsNull);
            }

            Rows.Add(row);
        }
    }

    /// <summary>
    /// Returns saved NPC if it still passes the tolerance check; otherwise records a
    /// mismatch and falls back to the saved value (the user is asked before any auto
    /// reassignment). When nothing is saved, runs FindFirstNpcAtWeight.
    /// </summary>
    private FormKey ResolveOrAuto(FormKey saved, Gender gender, int weight, int tolerance, bool isExplicitSave)
    {
        if (isExplicitSave && !saved.IsNull)
        {
            var actual = _previewNpcResolver.GetNpcWeight(saved);
            if (actual.HasValue && System.Math.Abs(actual.Value - weight) <= tolerance)
            {
                return saved;
            }
            // Saved NPC's weight has drifted (or NPC no longer resolvable). Keep it for now,
            // surface a mismatch entry so the user can decide.
            PendingMismatches.Add(new MismatchedPreviewEntry
            {
                Weight = weight,
                Gender = gender,
                SavedNpc = saved,
                ActualWeight = actual,
            });
            return saved;
        }

        return _previewNpcResolver.FindFirstNpcAtWeight(gender, weight, tolerance);
    }

    public OBodyPreviewNpcSettings DumpToModel()
    {
        var model = new OBodyPreviewNpcSettings();
        foreach (var row in Rows)
        {
            model.WeightPreviewNpcs[row.Weight] = new PreviewNpcPair
            {
                MaleNpc = row.MaleNpc,
                FemaleNpc = row.FemaleNpc,
            };
        }
        return model;
    }

    /// <summary>
    /// Returns the configured preview NPC for the given weight + gender. Falls back to
    /// FormKey.Null when the weight slot has no row or no NPC for that gender — callers
    /// should treat null as "viewer should not load anything".
    /// </summary>
    public FormKey ResolveNpc(int weight, Gender gender)
    {
        var row = Rows.FirstOrDefault(r => r.Weight == weight);
        if (row == null) return FormKey.Null;
        return gender == Gender.Female ? row.FemaleNpc : row.MaleNpc;
    }

    /// <summary>
    /// Re-runs FindFirstNpcAtWeight for the supplied entries (called after the user
    /// answers Yes to the mismatch popup). Updates the matching rows in place.
    /// </summary>
    public void AutoReassign(IEnumerable<MismatchedPreviewEntry> entries, int tolerance = 5)
    {
        foreach (var entry in entries)
        {
            var row = Rows.FirstOrDefault(r => r.Weight == entry.Weight);
            if (row == null) continue;
            var replacement = _previewNpcResolver.FindFirstNpcAtWeight(entry.Gender, entry.Weight, tolerance);
            if (entry.Gender == Gender.Female) row.FemaleNpc = replacement;
            else row.MaleNpc = replacement;
        }
    }

    /// <summary>Used by the per-row Auto buttons.</summary>
    public FormKey AutoResolve(Gender gender, int weight, int tolerance = 5)
    {
        return _previewNpcResolver.FindFirstNpcAtWeight(gender, weight, tolerance);
    }

    private void ResetAllRows(int tolerance = 5)
    {
        foreach (var row in Rows)
        {
            if (row.MaleApplicable)
            {
                row.MaleNpc = _previewNpcResolver.FindFirstNpcAtWeight(Gender.Male, row.Weight, tolerance);
            }
            if (row.FemaleApplicable)
            {
                row.FemaleNpc = _previewNpcResolver.FindFirstNpcAtWeight(Gender.Female, row.Weight, tolerance);
            }
        }
    }

    private static HashSet<int> CollectWeightSlots(IEnumerable<BodySlideSetting> presets)
    {
        var result = new HashSet<int>();
        if (presets == null) return result;
        foreach (var preset in presets)
        {
            if (preset?.BodyShapeDescriptorsByWeight == null) continue;
            foreach (var w in preset.BodyShapeDescriptorsByWeight.Keys)
            {
                result.Add(w);
            }
        }
        return result;
    }
}

/// <summary>
/// One row of the per-weight preview NPC grid. <see cref="MaleApplicable"/> /
/// <see cref="FemaleApplicable"/> drive UI dimming for slots that no preset on
/// that gender ever uses.
/// </summary>
public class VM_OBodyPreviewWeightRow : VM
{
    private readonly VM_OBodyPreviewNpcSettings _parent;

    public VM_OBodyPreviewWeightRow(int weight, VM_OBodyPreviewNpcSettings parent)
    {
        _parent = parent;
        Weight = weight;

        parent.WhenAnyValue(x => x.lk)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        AutoMaleCommand = new RelayCommand(
            canExecute: _ => MaleApplicable,
            execute: _ => MaleNpc = parent.AutoResolve(Gender.Male, Weight)
        );
        AutoFemaleCommand = new RelayCommand(
            canExecute: _ => FemaleApplicable,
            execute: _ => FemaleNpc = parent.AutoResolve(Gender.Female, Weight)
        );
    }

    public int Weight { get; }
    public FormKey MaleNpc { get; set; } = FormKey.Null;
    public FormKey FemaleNpc { get; set; } = FormKey.Null;

    /// <summary>True if at least one male preset has this weight slot.</summary>
    public bool MaleApplicable { get; set; } = true;
    /// <summary>True if at least one female preset has this weight slot.</summary>
    public bool FemaleApplicable { get; set; } = true;

    public ILinkCache lk { get; private set; }
    public IEnumerable<System.Type> NPCPickerFormKeys => _parent.NPCPickerFormKeys;

    public RelayCommand AutoMaleCommand { get; }
    public RelayCommand AutoFemaleCommand { get; }
}

/// <summary>
/// Detail of a stored preview NPC whose recorded weight no longer matches its weight slot.
/// Used to populate the mismatch-confirmation popup (B3).
/// </summary>
public class MismatchedPreviewEntry
{
    public int Weight { get; set; }
    public Gender Gender { get; set; }
    public FormKey SavedNpc { get; set; }
    public float? ActualWeight { get; set; }
}
