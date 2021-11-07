﻿using System;
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
            this.GroupName = source.groupName;
            this.Gender = source.gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
        }

        public FlattenedAssetPack(string groupName, Gender gender)
        {
            this.GroupName = groupName;
            this.Gender = gender;
            this.Subgroups = new List<List<FlattenedSubgroup>>();
        }

        public string GroupName { get; set; }
        public Gender Gender { get; set; }
        public List<List<FlattenedSubgroup>> Subgroups { get; set; }


        public static FlattenedAssetPack FlattenAssetPack(AssetPack source, List<RaceGrouping> raceGroupingList, bool includeBodyGen)
        {
            var output = new FlattenedAssetPack(source);

            for (int i = 0; i < source.subgroups.Count; i++)
            {
                var flattenedSubgroups = new List<FlattenedSubgroup>();
                FlattenedSubgroup.FlattenSubgroups(source.subgroups[i], null, flattenedSubgroups, raceGroupingList, output.GroupName, i, includeBodyGen, source.subgroups);
                output.Subgroups.Add(flattenedSubgroups);
            }

            return output;
        }

        public FlattenedAssetPack ShallowCopy()
        {
            FlattenedAssetPack copy = new FlattenedAssetPack(this.GroupName, this.Gender);
            foreach (var subgroupHashSet in this.Subgroups)
            {
                var copiedList = new List<FlattenedSubgroup>();
                foreach (var subgroup in subgroupHashSet)
                {
                    copiedList.Add(subgroup);
                }
                copy.Subgroups.Add(copiedList);
            }
            return copy;
        }
    }
}