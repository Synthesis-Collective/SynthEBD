using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_LinkedNPCGroup
    {
        public VM_LinkedNPCGroup()
        {
            this.GroupName = "";
            this.NPCFormKeys = new ObservableCollection<FormKey>();
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();
        }

        public string GroupName { get; set; }
        public ObservableCollection<FormKey> NPCFormKeys { get; set; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public static ObservableCollection<VM_LinkedNPCGroup> GetViewModelsFromModels(HashSet<LinkedNPCGroup> models)
        {
            var viewModels = new ObservableCollection<VM_LinkedNPCGroup>();
            foreach (var m in models)
            {
                VM_LinkedNPCGroup vm = new VM_LinkedNPCGroup();
                vm.GroupName = m.GroupName;
                vm.NPCFormKeys = new ObservableCollection<FormKey>(m.NPCFormKeys);
                viewModels.Add(vm);
            }
            return viewModels;
        }

        public static void DumpViewModelsToModels(HashSet<LinkedNPCGroup> models, ObservableCollection<VM_LinkedNPCGroup> viewModels)
        {
            models.Clear();

            foreach (var vm in viewModels)
            {
                LinkedNPCGroup m = new LinkedNPCGroup();
                m.GroupName = vm.GroupName;
                m.NPCFormKeys = vm.NPCFormKeys.ToHashSet();
                models.Add(m);
            }
        }
    }
}
