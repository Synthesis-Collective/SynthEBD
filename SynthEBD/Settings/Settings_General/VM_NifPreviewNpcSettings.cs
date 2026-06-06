using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// General-settings section mapping each PatchableRace → (male preview NPC,
/// female preview NPC) for the 3D Character Viewer in contexts outside of
/// Specific NPC Assignment (Textures &amp; Meshes render, Headparts).
/// Also stores a "Default" fallback pair used when a race is not mapped.
/// </summary>
public class VM_NifPreviewNpcSettings : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PreviewNpcResolver _previewNpcResolver;
    private readonly Logger _logger;

    /// <summary>Mirrors the environment link cache and seeds the special "Default" fallback row.</summary>
    public VM_NifPreviewNpcSettings(
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

        DefaultRow = new VM_RaceNpcRow(FormKey.Null, this) { IsDefault = true };
    }

    public ObservableCollection<VM_RaceNpcRow> Rows { get; } = new();
    public VM_RaceNpcRow DefaultRow { get; }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

    /// <summary>
    /// Rebuild Rows from the current PatchableRaces list, merging any saved
    /// assignments and auto-populating unset entries.
    /// </summary>
    public void CopyInFromModel(NifPreviewNpcSettings model, IEnumerable<FormKey> patchableRaces)
    {
        Rows.Clear();

        if (model == null) model = new NifPreviewNpcSettings();

        // Default row
        DefaultRow.MaleNpc = !model.DefaultNpcs.MaleNpc.IsNull
            ? model.DefaultNpcs.MaleNpc
            : _previewNpcResolver.FindFirstNordRaceNpc(Gender.Male);
        DefaultRow.FemaleNpc = !model.DefaultNpcs.FemaleNpc.IsNull
            ? model.DefaultNpcs.FemaleNpc
            : _previewNpcResolver.FindFirstNordRaceNpc(Gender.Female);

        // Per-race rows
        foreach (var race in patchableRaces)
        {
            var row = new VM_RaceNpcRow(race, this);

            var saved = model.RacePreviewNpcs.FirstOrDefault(e => e.Race.Equals(race));
            if (saved != null)
            {
                row.MaleNpc = !saved.MaleNpc.IsNull
                    ? saved.MaleNpc
                    : _previewNpcResolver.FindFirstNpcForRace(race, Gender.Male);
                row.FemaleNpc = !saved.FemaleNpc.IsNull
                    ? saved.FemaleNpc
                    : _previewNpcResolver.FindFirstNpcForRace(race, Gender.Female);
            }
            else
            {
                row.MaleNpc = _previewNpcResolver.FindFirstNpcForRace(race, Gender.Male);
                row.FemaleNpc = _previewNpcResolver.FindFirstNpcForRace(race, Gender.Female);
            }

            Rows.Add(row);
        }
    }

    /// <summary>VM → Model: writes the default pair and every per-race row back to a new <see cref="NifPreviewNpcSettings"/>.</summary>
    public NifPreviewNpcSettings DumpToModel()
    {
        var model = new NifPreviewNpcSettings
        {
            DefaultNpcs = new PreviewNpcPair
            {
                MaleNpc = DefaultRow.MaleNpc,
                FemaleNpc = DefaultRow.FemaleNpc,
            }
        };

        foreach (var row in Rows)
        {
            model.RacePreviewNpcs.Add(new RacePreviewEntry
            {
                Race = row.Race,
                MaleNpc = row.MaleNpc,
                FemaleNpc = row.FemaleNpc,
            });
        }
        return model;
    }

    /// <summary>
    /// Returns the configured preview NPC for the given race+gender. Falls
    /// back to DefaultRow if the race is unmapped or the row's NPC is null.
    /// </summary>
    public FormKey ResolveNpc(FormKey race, Gender gender)
    {
        var row = Rows.FirstOrDefault(r => r.Race.Equals(race));
        FormKey pick = FormKey.Null;
        if (row != null)
        {
            pick = gender == Gender.Female ? row.FemaleNpc : row.MaleNpc;
        }
        if (pick.IsNull)
        {
            pick = gender == Gender.Female ? DefaultRow.FemaleNpc : DefaultRow.MaleNpc;
        }
        return pick;
    }
}

/// <summary>
/// One row of <see cref="VM_NifPreviewNpcSettings"/>: a single race mapped to its male and
/// female preview NPCs (or the synthetic "Default" row when <see cref="IsDefault"/> is set).
/// </summary>
public class VM_RaceNpcRow : VM
{
    private readonly VM_NifPreviewNpcSettings _parent;

    /// <summary>Records the race and mirrors the parent's link cache for race-name resolution.</summary>
    public VM_RaceNpcRow(FormKey race, VM_NifPreviewNpcSettings parent)
    {
        _parent = parent;
        Race = race;

        parent.WhenAnyValue(x => x.lk)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
    }

    public FormKey Race { get; }
    public FormKey MaleNpc { get; set; } = FormKey.Null;
    public FormKey FemaleNpc { get; set; } = FormKey.Null;
    public bool IsDefault { get; set; } = false;

    /// <summary>Display label for the row: "Default", "(none)", or the race's EditorID/FormKey resolved via the link cache.</summary>
    public string RaceDisplay
    {
        get
        {
            if (IsDefault) return "Default";
            if (Race.IsNull) return "(none)";
            if (_parent.lk != null && _parent.lk.TryResolve<IRaceGetter>(Race, out var rec))
            {
                return rec.EditorID ?? Race.ToString();
            }
            return Race.ToString();
        }
    }

    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCPickerFormKeys => _parent.NPCPickerFormKeys;
}
