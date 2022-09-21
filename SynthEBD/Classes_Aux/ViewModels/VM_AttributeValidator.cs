using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_AttributeValidator : VM
    {
        public VM_AttributeValidator(VM_NPCAttribute trialAttribute, ObservableCollection<VM_AttributeGroup> attGroupVMs)
        {
            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

            TrialAttribute = VM_NPCAttribute.DumpViewModelToModel(trialAttribute);
            AttributeGroups = attGroupVMs.Select(x => VM_AttributeGroup.DumpViewModelToModel(x)).ToHashSet();

            if (trialAttribute is not null && trialAttribute.GroupedSubAttributes.Any())
            {
                HasRestrictions = true;
                RestrictionColor = new SolidColorBrush(Colors.Green);
            }
            
            this.WhenAnyValue(x => x.NPCformkey).Subscribe(x => TestNPC());
        }

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
        public FormKey NPCformkey { get; set; }
        public NPCAttribute TrialAttribute { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
        public bool HasRestrictions { get; set; } = false;
        public SolidColorBrush RestrictionColor { get; set; } = new SolidColorBrush(Colors.Yellow);
        public bool MatchesRestrictions { get; set; }
        public SolidColorBrush MatchColor { get; set; } = new SolidColorBrush(Colors.Yellow);
        public int MatchedForceIfs { get; set; }
        public SolidColorBrush ForceIfColor { get; set; } = new SolidColorBrush(Colors.Yellow);
        public string MatchedLog { get; set; }
        public string UnMatchedLog { get; set; }
        public string ForceIfLog { get; set; }


        public void TestNPC()
        {
            var attList = new HashSet<NPCAttribute>() { TrialAttribute };

            if (lk.TryResolve<INpcGetter>(NPCformkey, out var npc))
            {
                AttributeMatcher.MatchNPCtoAttributeList(attList, npc, AttributeGroups, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfAttributeWeightedCount, out string matchLog, out string unmatchedLog, out string forceIfLog, null);
                HasRestrictions = hasAttributeRestrictions;
                MatchesRestrictions = matchesAttributeRestrictions;
                MatchedLog = matchLog;
                UnMatchedLog = unmatchedLog;
                ForceIfLog = forceIfLog;

                if (matchedForceIfAttributeWeightedCount > 0)
                {
                    ForceIfColor = new SolidColorBrush(Colors.Green);
                }
            }

            if (!HasRestrictions)
            {
                RestrictionColor = new SolidColorBrush(Colors.Red);
                MatchColor = new SolidColorBrush(Colors.Yellow);
                ForceIfColor = new SolidColorBrush(Colors.Yellow);
            }
            else if (MatchesRestrictions)
            {
                MatchColor = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MatchColor = new SolidColorBrush(Colors.Red);
            }  
        }
    }
}
