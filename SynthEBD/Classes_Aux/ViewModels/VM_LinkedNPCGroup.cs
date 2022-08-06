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

public class VM_LinkedNPCGroup : VM
{
    public VM_LinkedNPCGroup()
    {
        PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

        Observable.CombineLatest(
                this.NPCFormKeys.ToObservableChangeSet()
                    .QueryWhenChanged(q => q),
                this.WhenAnyValue(x => x.lk),
            (formKeys, linkCache) =>
            {
                var ret = new HashSet<string>();
                foreach (var fk in formKeys)
                {
                    if (linkCache.TryResolve<INpcGetter>(fk, out var npcGetter))
                    {
                        ret.Add(Logger.GetNPCLogNameString(npcGetter));
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

    public static ObservableCollection<VM_LinkedNPCGroup> GetViewModelsFromModels(List<LinkedNPCGroup> models)
    {
        var viewModels = new ObservableCollection<VM_LinkedNPCGroup>();
        foreach (var m in models)
        {
            VM_LinkedNPCGroup vm = new VM_LinkedNPCGroup();
            vm.GroupName = m.GroupName;
            vm.NPCFormKeys.SetTo(m.NPCFormKeys, checkEquality: false);
            if ((m.Primary == null || m.Primary.IsNull) && PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<INpcGetter>(m.NPCFormKeys.FirstOrDefault(), out var primaryNPC))
            {
                vm.Primary = Logger.GetNPCLogNameString(primaryNPC);
            }
            else if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<INpcGetter>(m.Primary, out var assignedPrimary))
            {
                vm.Primary = Logger.GetNPCLogNameString(assignedPrimary);
            }

            viewModels.Add(vm);
        }
        return viewModels;
    }

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
                var fkString = vm.Primary.Split('|')[2];
                if (FormKey.TryFactory(fkString, out var primary))
                {
                    m.Primary = primary;
                }
            }
            models.Add(m);
        }
    }
}