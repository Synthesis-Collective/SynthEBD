using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_LinkedNPCGroup : INotifyPropertyChanged
    {
        public VM_LinkedNPCGroup()
        {
            this.GroupName = "";
            this.NPCFormKeys = new ObservableCollection<FormKey>();
            this.lk = GameEnvironmentProvider.MyEnvironment.LinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();

            this.PrimaryCandidates = new HashSet<string>();
            this.PropertyChanged += RefereshPrimaryAssignment;
            this.NPCFormKeys.CollectionChanged += RefereshPrimaryAssignment;
        }

        public string GroupName { get; set; }
        public ObservableCollection<FormKey> NPCFormKeys { get; set; }
        public ILinkCache lk { get; set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; }
        public string Primary { get; set; }
        public HashSet<string> PrimaryCandidates { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RefereshPrimaryAssignment(object sender, PropertyChangedEventArgs e)
        {
            RefereshPrimaryAssignment();
        }

        public void RefereshPrimaryAssignment(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefereshPrimaryAssignment();
        }

        public void RefereshPrimaryAssignment()
        {
            this.PrimaryCandidates.Clear();
            foreach (var fk in this.NPCFormKeys)
            {
                if (lk.TryResolve<INpcGetter>(fk, out var npcGetter))
                {
                    this.PrimaryCandidates.Add(Logger.GetNPCLogNameString(npcGetter));
                }
            }
        }

        public static ObservableCollection<VM_LinkedNPCGroup> GetViewModelsFromModels(HashSet<LinkedNPCGroup> models)
        {
            var viewModels = new ObservableCollection<VM_LinkedNPCGroup>();
            foreach (var m in models)
            {
                VM_LinkedNPCGroup vm = new VM_LinkedNPCGroup();
                vm.GroupName = m.GroupName;
                vm.NPCFormKeys = new ObservableCollection<FormKey>(m.NPCFormKeys);
                if ((m.Primary == null || m.Primary.IsNull) && GameEnvironmentProvider.MyEnvironment.LinkCache.TryResolve<INpcGetter>(m.NPCFormKeys.FirstOrDefault(), out var primaryNPC))
                {
                    vm.Primary = Logger.GetNPCLogNameString(primaryNPC);
                }
                else if (GameEnvironmentProvider.MyEnvironment.LinkCache.TryResolve<INpcGetter>(m.Primary, out var assignedPrimary))
                {
                    vm.Primary = Logger.GetNPCLogNameString(assignedPrimary);
                }

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

                var fkString = vm.Primary.Split('|')[2];
                if (FormKey.TryFactory(fkString, out var primary))
                {
                    m.Primary = primary;
                }    

                models.Add(m);
            }
        }
    }
}
