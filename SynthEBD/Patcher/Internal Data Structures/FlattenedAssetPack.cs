using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FlattenedAssetPack
    {
        public FlattenedAssetPack(AssetPack source)
        {
            this.GroupName = source.GroupName;
            this.Gender = source.Gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = source.DefaultRecordTemplate;
            this.AdditionalRecordTemplateAssignments = source.AdditionalRecordTemplateAssignments;
            this.AssociatedBodyGenConfigName = source.AssociatedBodyGenConfigName;
            this.Source = source;
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type = AssetPackType.Primary;
        }

        public FlattenedAssetPack(string groupName, Gender gender, FormKey defaultRecordTemplate, HashSet<AdditionalRecordTemplate> additionalRecordTemplateAssignments, string associatedBodyGenConfigName, AssetPack source)
        {
            this.GroupName = groupName;
            this.Gender = gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = defaultRecordTemplate;
            this.AdditionalRecordTemplateAssignments = additionalRecordTemplateAssignments;
            this.AssociatedBodyGenConfigName = associatedBodyGenConfigName;
            this.Source = source;
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type = AssetPackType.Primary;
        }

        public FlattenedAssetPack()
        {
            this.GroupName = "";
            this.Gender = Gender.Male;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = new FormKey();
            this.AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
            this.AssociatedBodyGenConfigName = "";
            this.Source = new AssetPack();
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type= AssetPackType.Primary;
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }
        public FormKey DefaultRecordTemplate { get; set; }
        public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
        public string AssociatedBodyGenConfigName { get; set; }
        public AssetPack Source { get; set; }
        public List<FlattenedReplacerGroup> AssetReplacerGroups { get; set; }
        public AssetPackType Type { get; set; }

        public enum AssetPackType
        {
            Primary,
            MixIn,
            ReplacerVirtual
        }

        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList)
        {
            var output = new FlattenedAssetPack(source);

            for (int i = 0; i < source.Subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, output.GroupName, i,  source.Subgroups, output);
                output.Subgroups.Add(flattenedSubgroups);
            }

            for (int i = 0; i < source.ReplacerGroups.Count; i++)
            {
                output.AssetReplacerGroups.Add(FlattenedReplacerGroup.FlattenReplacerGroup(source.ReplacerGroups[i], raceGroupingList, output));
            }

            return output;
        }

        public FlattenedAssetPack ShallowCopy()
        {
            FlattenedAssetPack copy = new FlattenedAssetPack(this.GroupName, this.Gender, this.DefaultRecordTemplate, this.AdditionalRecordTemplateAssignments, this.AssociatedBodyGenConfigName, this.Source);
            foreach (var subgroupList in this.Subgroups)
            {
                copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
            }
            foreach (var replacer in this.AssetReplacerGroups)
            {
                copy.AssetReplacerGroups.Add(replacer.ShallowCopy());
            }
            return copy;
        }

        public static FlattenedAssetPack CreateVirtualFromReplacerGroup(FlattenedReplacerGroup source)
        {
            FlattenedAssetPack virtualFAP = new FlattenedAssetPack();
            virtualFAP.GroupName = source.GroupName; // this is the name of the replacer group, not the asset pack
            foreach (var subgroupsAtPos in source.Subgroups)
            {
                virtualFAP.Subgroups.Add(subgroupsAtPos);
            }
            virtualFAP.Source = source.Source;
            virtualFAP.Type = AssetPackType.ReplacerVirtual;
            return virtualFAP;
        }
    }
}
