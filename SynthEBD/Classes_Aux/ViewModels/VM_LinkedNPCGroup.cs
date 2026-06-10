using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;

namespace SynthEBD;

/// <summary>
/// View model for a linked-NPC group (a set of NPCs that share assignments, anchored to a primary).
/// Reactively recomputes the human-readable primary-candidate names as the member list or link cache changes.
/// </summary>
public class VM_LinkedNPCGroup : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    /// <summary>Autofac factory delegate for constructing an empty linked-NPC-group VM.</summary>
    public delegate VM_LinkedNPCGroup Factory();
    /// <summary>Creates the VM and wires reactive recomputation of <see cref="PrimaryCandidates"/> from the member list and link cache.</summary>
    /// <param name="environmentProvider">Supplies the link cache for resolving member NPCs.</param>
    /// <param name="logger">Logger used to format NPC display names.</param>
    public VM_LinkedNPCGroup(IEnvironmentStateProvider environmentProvider, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);
        _logger = logger;

        Observable.CombineLatest(
                NPCFormKeys.ToObservableChangeSet()
                    .QueryWhenChanged(q => q),
                this.WhenAnyValue(x => x.lk),
            (formKeys, linkCache) =>
            {
                var ret = new HashSet<string>();
                foreach (var fk in formKeys)
                {
                    if (linkCache.TryResolve<INpcGetter>(fk, out var npcGetter))
                    {
                        ret.Add(_logger.GetNPCLogNameString(npcGetter));
                    }
                }

                return (IReadOnlyCollection<string>)ret;
            })
            .Subscribe(x => PrimaryCandidates = x)
            .DisposeWith(this);
    }

    public string GroupName { get; set; } = "";
    public ObservableCollection<FormKey> NPCFormKeys { get; } = new();
    
    public ILinkCache lk { get; private set; }
    public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
    public string Primary { get; set; }

    public IReadOnlyCollection<string> PrimaryCandidates { get; private set; }

    /// <summary>Builds linked-group VMs from models, resolving each group's primary NPC display name (falling back to the first member when none is set).</summary>
    /// <param name="models">The group models.</param>
    /// <param name="factory">Factory used to construct each VM.</param>
    /// <param name="linkCache">Link cache for resolving NPCs.</param>
    /// <param name="logger">Logger used to format NPC display names.</param>
    /// <returns>A collection of linked-group view models.</returns>
    public static ObservableCollection<VM_LinkedNPCGroup> GetViewModelsFromModels(List<LinkedNPCGroup> models, VM_LinkedNPCGroup.Factory factory, ILinkCache linkCache, Logger logger)
    {
        var viewModels = new ObservableCollection<VM_LinkedNPCGroup>();
        foreach (var m in models)
        {
            VM_LinkedNPCGroup vm = factory();
            vm.GroupName = m.GroupName;
            vm.NPCFormKeys.SetTo(m.NPCFormKeys, checkEquality: false);
            if ((m.Primary == null || m.Primary.IsNull) && linkCache.TryResolve<INpcGetter>(m.NPCFormKeys.FirstOrDefault(), out var primaryNPC))
            {
                vm.Primary = Logger.GetNPCLogNameString(primaryNPC, logger);
            }
            else if (linkCache.TryResolve<INpcGetter>(m.Primary, out var assignedPrimary))
            {
                vm.Primary = Logger.GetNPCLogNameString(assignedPrimary, logger);
            }

            viewModels.Add(vm);
        }
        return viewModels;
    }

    /// <summary>Returns the last '|'-delimited field of a display string, trimmed.</summary>
    /// <param name="value">The display string (e.g. "Name | EditorID | FormKey").</param>
    /// <returns>The trailing field, trimmed; the whole trimmed string when there is no '|'.</returns>
    /// <remarks>The FormKey is always the last field (EditorID/FormKey cannot contain '|'), so taking the last
    /// field is robust to a '|' inside an earlier field (e.g. an NPC name) and to malformed strings -- the caller's
    /// <c>FormKey.TryFactory</c> then degrades gracefully instead of mis-indexing or throwing (B51).</remarks>
    public static string GetTrailingPipeField(string value)
    {
        return value.Split('|').Last().Trim();
    }

    /// <summary>Replaces the contents of <paramref name="models"/> with models projected from the given VMs, parsing each primary's FormKey out of its "Name | EditorID | FormKey" display string.</summary>
    /// <param name="models">Target model list (cleared and repopulated).</param>
    /// <param name="viewModels">Source view models.</param>
    public static void DumpViewModelsToModels(List<LinkedNPCGroup> models, ObservableCollection<VM_LinkedNPCGroup> viewModels)
    {
        models.Clear();

        foreach (var vm in viewModels)
        {
            LinkedNPCGroup m = new LinkedNPCGroup();
            m.GroupName = vm.GroupName;
            m.NPCFormKeys = vm.NPCFormKeys.ToHashSet();

            if (vm.Primary != null && vm.Primary.Any())
            {
                var fkString = GetTrailingPipeField(vm.Primary);
                if (FormKey.TryFactory(fkString, out var primary))
                {
                    m.Primary = primary;
                }
            }
            models.Add(m);
        }
    }
}