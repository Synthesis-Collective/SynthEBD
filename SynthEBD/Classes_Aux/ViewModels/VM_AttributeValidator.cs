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
    /// <summary>
    /// View model for the attribute-validator UI: takes a trial attribute and a test NPC, runs it through the
    /// <see cref="AttributeMatcher"/>, and surfaces the match / restriction / force-if results with status colors.
    /// </summary>
    public class VM_AttributeValidator : VM
    {
        private readonly PatcherState _patcherState;
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly AttributeMatcher _attributeMatcher;
        /// <summary>Creates the validator from a trial attribute and the available groups, re-testing whenever the chosen NPC changes.</summary>
        /// <param name="trialAttribute">The attribute being validated (dumped to a model).</param>
        /// <param name="attGroupVMs">Attribute groups available for resolution.</param>
        /// <param name="patcherState">Patcher state (for race aliases).</param>
        /// <param name="environmentProvider">Supplies the link cache.</param>
        /// <param name="attributeMatcher">The matcher used to evaluate the attribute against the NPC.</param>
        public VM_AttributeValidator(VM_NPCAttribute trialAttribute, ObservableCollection<VM_AttributeGroup> attGroupVMs, PatcherState patcherState, IEnvironmentStateProvider environmentProvider, AttributeMatcher attributeMatcher)
        {
            _patcherState = patcherState;
            _environmentProvider = environmentProvider;
            _attributeMatcher = attributeMatcher;

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => lk = x)
            .DisposeWith(this);

            TrialAttribute = trialAttribute.DumpViewModelToModel();
            AttributeGroups = attGroupVMs.Select(x => VM_AttributeGroup.DumpViewModelToModel(x)).ToHashSet();

            if (trialAttribute is not null && trialAttribute.GroupedSubAttributes.Any())
            {
                HasRestrictions = true;
                RestrictionColor = CommonColors.Green;
            }
            
            this.WhenAnyValue(x => x.NPCformkey).Subscribe(x => TestNPC()).DisposeWith(this);
        }

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
        public FormKey NPCformkey { get; set; }
        public NPCAttribute TrialAttribute { get; set; }
        public HashSet<AttributeGroup> AttributeGroups { get; set; } = new();
        public bool HasRestrictions { get; set; } = false;
        public SolidColorBrush RestrictionColor { get; set; } = CommonColors.Yellow;
        public bool MatchesRestrictions { get; set; }
        public SolidColorBrush MatchColor { get; set; } = CommonColors.Yellow;
        public int MatchedForceIfs { get; set; }
        public SolidColorBrush ForceIfColor { get; set; } = CommonColors.Yellow;
        public string MatchedLog { get; set; }
        public string UnMatchedLog { get; set; }
        public string ForceIfLog { get; set; }
        /// <summary>Resolves the selected NPC, applies any race alias, runs the matcher against the trial attribute, and updates the result fields and status colors.</summary>
        public void TestNPC()
        {
            var attList = new HashSet<NPCAttribute>() { TrialAttribute };

            if (lk.TryResolve<INpcGetter>(NPCformkey, out var npc))
            {
                var npcRaceFormKey = npc.Race.FormKey;
                var raceAlias = _patcherState.GeneralSettings.RaceAliases.FirstOrDefault(x => x.Race == npcRaceFormKey);
                if (raceAlias != null)
                {
                    npcRaceFormKey = raceAlias.AliasRace;
                }
                _attributeMatcher.MatchNPCtoAttributeList(attList, npc, npcRaceFormKey, AttributeGroups, true, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfAttributeWeightedCount, out string matchLog, out string unmatchedLog, out string forceIfLog, null);
                HasRestrictions = hasAttributeRestrictions;
                MatchesRestrictions = matchesAttributeRestrictions;
                MatchedLog = matchLog;
                UnMatchedLog = unmatchedLog;
                ForceIfLog = forceIfLog;

                if (matchedForceIfAttributeWeightedCount > 0)
                {
                    ForceIfColor = CommonColors.Green;
                }
            }

            if (!HasRestrictions)
            {
                RestrictionColor = CommonColors.Red;
                MatchColor = CommonColors.Yellow;
                ForceIfColor = CommonColors.Yellow;
            }
            else if (MatchesRestrictions)
            {
                MatchColor = CommonColors.Green;
            }
            else
            {
                MatchColor = CommonColors.Red;
            }  
        }
    }
}
