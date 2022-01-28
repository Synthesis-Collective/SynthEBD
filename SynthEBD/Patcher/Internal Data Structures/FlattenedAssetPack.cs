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
        public FlattenedAssetPack(AssetPack source, AssetPackType type)
        {
            this.GroupName = source.GroupName;
            this.Gender = source.Gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = source.DefaultRecordTemplate;
            this.AdditionalRecordTemplateAssignments = source.AdditionalRecordTemplateAssignments;
            this.AssociatedBodyGenConfigName = source.AssociatedBodyGenConfigName;
            this.Source = source;
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type = type;
            this.ReplacerName = "";
        }

        public FlattenedAssetPack(string groupName, Gender gender, FormKey defaultRecordTemplate, HashSet<AdditionalRecordTemplate> additionalRecordTemplateAssignments, string associatedBodyGenConfigName, AssetPack source, AssetPackType type)
        {
            this.GroupName = groupName;
            this.Gender = gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = defaultRecordTemplate;
            this.AdditionalRecordTemplateAssignments = additionalRecordTemplateAssignments;
            this.AssociatedBodyGenConfigName = associatedBodyGenConfigName;
            this.Source = source;
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type = type;
            this.ReplacerName = "";
        }

        public FlattenedAssetPack(AssetPackType type)
        {
            this.GroupName = "";
            this.Gender = Gender.Male;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = new FormKey();
            this.AdditionalRecordTemplateAssignments = new HashSet<AdditionalRecordTemplate>();
            this.AssociatedBodyGenConfigName = "";
            this.Source = new AssetPack();
            this.AssetReplacerGroups = new List<FlattenedReplacerGroup>();
            this.Type = type;
            this.ReplacerName = "";
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
        public string ReplacerName { get; set; } // only used when Type == ReplacerVirtual

        public enum AssetPackType
        {
            Primary,
            MixIn,
            ReplacerVirtual
        }

        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList)
        {
            FlattenedAssetPack output = null;
            if (source.ConfigType == SynthEBD.AssetPackType.MixIn)
            {
                output = new FlattenedAssetPack(source, AssetPackType.MixIn);
            }
            else
            {
                output = new FlattenedAssetPack(source, AssetPackType.Primary);
            }

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
            FlattenedAssetPack copy = new FlattenedAssetPack(this.GroupName, this.Gender, this.DefaultRecordTemplate, this.AdditionalRecordTemplateAssignments, this.AssociatedBodyGenConfigName, this.Source, this.Type);
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

        public static FlattenedAssetPack CreateVirtualFromReplacerGroup(FlattenedReplacerGroup source, string sourceAssetPackName)
        {
            FlattenedAssetPack virtualFAP = new FlattenedAssetPack(AssetPackType.ReplacerVirtual);
            virtualFAP.GroupName = source.Source.GroupName;
            virtualFAP.ReplacerName = source.Name;
            foreach (var subgroupsAtPos in source.Subgroups)
            {
                virtualFAP.Subgroups.Add(subgroupsAtPos);
            }
            virtualFAP.Source = source.Source;
            return virtualFAP;
        }
    }
}
