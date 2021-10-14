using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_SpecificNPCAssignment : INotifyPropertyChanged
    {
        public VM_SpecificNPCAssignment()
        {
            this.PropertyChanged += UpdateDispName;
            this.DispName = "";
            this.NPCFormKey = new FormKey();
            this.ForcedAssetPackName = "";
            this.ForcedSubgroups = new HashSet<VM_Subgroup>();
            this.ForcedHeight = "";
            this.ForcedBodyGenMorphs = new HashSet<VM_BodyGenTemplate>();

            this.lk = new GameEnvironmentProvider().MyEnvironment.LinkCache;
            this.NPCFormKeyTypes = typeof(INpcGetter).AsEnumerable();
        }

        public string DispName { get; set; }
        public FormKey NPCFormKey { get; set; }
        public string ForcedAssetPackName { get; set; }
        public HashSet<VM_Subgroup> ForcedSubgroups { get; set; }
        public string ForcedHeight { get; set; }
        public HashSet<VM_BodyGenTemplate> ForcedBodyGenMorphs { get; set; }

        public ILinkCache lk { get; set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_SpecificNPCAssignment GetViewModelFromModel(SpecificNPCAssignment model, ObservableCollection<VM_AssetPack> assetPacks, VM_SettingsBodyGen BGVM, IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> env)
        {
            var newVM = new VM_SpecificNPCAssignment();
            newVM.NPCFormKey = model.NPCFormKey;

            var npcFormLink = new FormLink<INpcGetter>(newVM.NPCFormKey);

            Gender gender = Gender.male;

            if (npcFormLink.TryResolve(env.LinkCache, out var npcRecord))
            {
                if (npcRecord.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
                {
                    gender = Gender.female;
                }
            }
            else
            {
                // Warn User
                return null;
            }

            bool assetPackFound = false;
            if (model.ForcedAssetPackName.Length == 0) { assetPackFound = true; }
            foreach (var ap in assetPacks)
            {
                if (ap.groupName == model.DispName)
                {
                    newVM.ForcedAssetPackName = ap.groupName;
                    assetPackFound = true;
                    
                    foreach (var id in model.ForcedSubgroupIDs)
                    {
                        var foundSubgroup = GetSubgroupByID(ap.subgroups, id);
                        if (foundSubgroup != null)
                        {
                            newVM.ForcedSubgroups.Add(foundSubgroup);
                            continue;
                        }
                        else
                        {
                            // Warn User
                        }
                    }
                }
            }
            if (assetPackFound == false) 
            { 
                // Warn user
            }

            newVM.ForcedHeight = model.ForcedHeight;

            ObservableCollection<VM_BodyGenTemplate> templates = new ObservableCollection<VM_BodyGenTemplate>();
            switch(gender)
            {
                case Gender.male:
                    templates = BGVM.CurrentMaleConfig.TemplateMorphUI.Templates;
                    break;
                case Gender.female:
                    templates = BGVM.CurrentFemaleConfig.TemplateMorphUI.Templates;
                    break;
            }

            foreach (var forcedMorph in model.ForcedBodyGenMorphNames)
            {
                bool morphFound = false;
                foreach (var morph in templates)
                {
                    if (morph.Label == forcedMorph)
                    {
                        newVM.ForcedBodyGenMorphs.Add(morph);
                        morphFound = true;
                        break; ;
                    }
                }
                if (morphFound == false)
                {
                    // Warn User
                }
            }

            newVM.DispName = createDispName(newVM.NPCFormKey);

            return newVM;
        }

        public static bool ContainsSubgroupID(ObservableCollection<VM_Subgroup> subgroups, string id)
        {
            foreach(var sg in subgroups)
            {
                if (sg.id == id) { return true; }
                else
                {
                    if (ContainsSubgroupID(sg.subgroups, id) == true) { return true; }
                }
            }
            return false;
        }

        public static VM_Subgroup GetSubgroupByID(ObservableCollection<VM_Subgroup> subgroups, string id)
        {
            foreach (var sg in subgroups)
            {
                if (sg.id == id) { return sg; }
                else
                {
                    var candidate = GetSubgroupByID(sg.subgroups, id);
                    if (candidate != null) { return candidate; }
                }
            }
            return null;
        }

        public void UpdateDispName(object sender, PropertyChangedEventArgs e)
        {
            this.DispName = createDispName(this.NPCFormKey);
        }

        public static string createDispName(FormKey NPCFormKey)
        {
            var npcFormLink = new FormLink<INpcGetter>(NPCFormKey);

            if (npcFormLink.TryResolve(new GameEnvironmentProvider().MyEnvironment.LinkCache, out var npcRecord))
            {
                string subName = "";
                if (npcRecord.Name.ToString().Length > 0)
                {
                    subName = npcRecord.Name.ToString();
                }
                else
                {
                    subName = npcRecord.EditorID;
                }
                return subName + " (" + NPCFormKey.ToString() + ")";
            }

            // Warn User
            return "";
        }
    }
}
