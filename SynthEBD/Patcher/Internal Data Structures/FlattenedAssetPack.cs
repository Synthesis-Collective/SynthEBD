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
        }

        public FlattenedAssetPack(string groupName, Gender gender, FormKey defaultRecordTemplate, HashSet<AdditionalRecordTemplate> additionalRecordTemplateAssignments, string associatedBodyGenConfigName)
        {
            this.GroupName = groupName;
            this.Gender = gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
            this.DefaultRecordTemplate = defaultRecordTemplate;
            this.AdditionalRecordTemplateAssignments = additionalRecordTemplateAssignments;
            this.AssociatedBodyGenConfigName = associatedBodyGenConfigName;
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }
        public FormKey DefaultRecordTemplate { get; set; }
        public HashSet<AdditionalRecordTemplate> AdditionalRecordTemplateAssignments { get; set; }
        public string AssociatedBodyGenConfigName { get; set; }

        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList, bool includeBodyGen)
        {
            var output = new FlattenedAssetPack(source);

            for (int i = 0; i < source.Subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.Subgroups[i], null, flattenedSubgroups, raceGroupingList, output.GroupName, i, includeBodyGen, source.Subgroups, output);
                output.Subgroups.Add(flattenedSubgroups);
            }

            return output;
        }

        public FlattenedAssetPack ShallowCopy()
        {
            FlattenedAssetPack copy = new FlattenedAssetPack(this.GroupName, this.Gender, this.DefaultRecordTemplate, this.AdditionalRecordTemplateAssignments, this.AssociatedBodyGenConfigName);
            foreach (var subgroupList in this.Subgroups)
            {
                copy.Subgroups.Add(new List<FlattenedSubgroup>(subgroupList));
            }
            return copy;
        }
    }
}
