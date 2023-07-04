using ICSharpCode.SharpZipLib.Zip;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using static SynthEBD.VM_Subgroup;

namespace SynthEBD
{
    public class VM_SubgroupLinker : VM
    {
        public VM_SubgroupLinker(AssetPack assetPack, AssetPack.Subgroup subgroup, Window_SubgroupLinker window)
        {
            _targetAssetPack = assetPack;
            _targetSubgroup = subgroup;

            if (!GetTopLevelIndex())
            {
                ///
            }

            Observable.CombineLatest(
                this.WhenAnyValue(x => x.IdToMatch),
                this.WhenAnyValue(x => x.NameToMatch),
                this.WhenAnyValue(x => x.IDcaseSensitive),
                this.WhenAnyValue(x => x.NameCaseSensitive),
                this.WhenAnyValue(x => x.IDallowPartial),
                this.WhenAnyValue(x => x.NameAllowPartial),
                (_, _, _, _, _, _) => { return 0; })
            .Subscribe(_ => CollectMatchingSubgroups()).DisposeWith(this);

            LinkThisTo = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => { 
                    foreach (var sg in CollectedSubgroups)
                    {
                        if (!_targetSubgroup.RequiredSubgroups.Contains(sg.ID))
                        {
                            _targetSubgroup.RequiredSubgroups.Add(sg.ID);
                        }
                    }
                    window.Close();
                }
            );

            LinkToThis = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    foreach (var sg in CollectedSubgroups)
                    {
                        if (!sg.RequiredSubgroups.Contains(_targetSubgroup.ID))
                        {
                            sg.RequiredSubgroups.Add(_targetSubgroup.ID);
                        }
                    }
                    window.Close();
                }
            );

            LinkWholeGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    var wholeSet = CollectedSubgroups.And(_targetSubgroup);

                    foreach (var sg in wholeSet)
                    {
                        foreach (var sg2 in wholeSet)
                        {
                            if (sg2.ID != sg.ID && !sg.RequiredSubgroups.Contains(sg2.ID))
                            {
                                sg.RequiredSubgroups.Add(sg2.ID);
                            }
                        }
                    }
                    window.Close();
                }
            );

            Close = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    window.Close();
                }
            );
        }

        private AssetPack _targetAssetPack;
        private AssetPack.Subgroup _targetSubgroup;
        private int _topLevelIndex = -1;
        public string IdToMatch { get; set; } = string.Empty;
        public string NameToMatch { get; set; } = string.Empty;
        public AndOr AndOrSelection { get; set; } = AndOr.Or;
        public bool IDcaseSensitive { get; set; } = false;
        public bool NameCaseSensitive { get; set; } = false;
        public bool IDallowPartial { get; set; } = false;
        public bool NameAllowPartial { get; set; } = false;

        public ObservableCollection<AssetPack.Subgroup> CollectedSubgroups { get; set; } = new(); // does not include _targetSubgroup

        public RelayCommand LinkThisTo { get; }
        public RelayCommand LinkToThis { get; }
        public RelayCommand LinkWholeGroup { get; }
        public RelayCommand Close { get; }

        private void CollectMatchingSubgroups()
        {
            CollectedSubgroups.Clear();

            for (int i = 0; i < _targetAssetPack.Subgroups.Count; i++)
            {
                if (i == _topLevelIndex)
                {
                    continue;
                }
                else
                {
                    CollectMatchingSubgroups(_targetAssetPack.Subgroups[i]);
                }
            }
        }

        private void CollectMatchingSubgroups(AssetPack.Subgroup subgroup)
        {
            if (bSubgroupMatches(subgroup))
            {
                CollectedSubgroups.Add(subgroup);
                return;
            }
            foreach (var sg in subgroup.Subgroups)
            {
                CollectMatchingSubgroups(sg);
            }
        }

        private bool bSubgroupMatches(AssetPack.Subgroup subgroup)
        {
            bool matchID = !IdToMatch.IsNullOrWhitespace();
            bool matchName = !NameToMatch.IsNullOrWhitespace();

            bool IdMatches = false;
            bool nameMatches = false;

            if (matchID)
            {
                switch(IDallowPartial)
                {
                    case false:
                        IdMatches = (IDcaseSensitive && IdToMatch == subgroup.ID) || (!IDcaseSensitive && IdToMatch.Equals(subgroup.ID, StringComparison.OrdinalIgnoreCase));
                        break;
                    case true:
                        IdMatches = (IDcaseSensitive && IdToMatch.Contains(subgroup.ID)) || (!IDcaseSensitive && subgroup.ID.Contains(IdToMatch, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }

            if (matchName)
            {
                switch(NameAllowPartial)
                {
                    case false:
                        nameMatches = (NameCaseSensitive && NameToMatch == subgroup.Name) || (!NameCaseSensitive && NameToMatch.Equals(subgroup.Name, StringComparison.OrdinalIgnoreCase));
                        break;
                    case true:
                        nameMatches = (NameCaseSensitive && NameToMatch.Contains(subgroup.Name)) || (!NameCaseSensitive && subgroup.Name.Contains(NameToMatch, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }

            switch(AndOrSelection)
            {
                case AndOr.And:
                    return IdMatches && nameMatches;
                case AndOr.Or:
                    return (matchID && IdMatches) || (matchName && nameMatches);
            }
            return false;
        }

        private bool GetTopLevelIndex()
        {
            for (int i = 0; i < _targetAssetPack.Subgroups.Count; i++)
            {
                var subgroup = _targetAssetPack.Subgroups[i];
                if (subgroup == _targetSubgroup || IndexContainsThisSubgroup(subgroup.Subgroups))
                {
                    _topLevelIndex = i;
                    return true;
                }
            }
            return false;
        }
        private bool IndexContainsThisSubgroup(List<AssetPack.Subgroup> subgroups)
        {
            if (subgroups.Contains(_targetSubgroup))
            {
                return true;
            }
            foreach (var sg in subgroups)
            {
                if (IndexContainsThisSubgroup(sg.Subgroups))
                {
                    return true;
                }
            }
            return false;
        }    
    }

    public enum AndOr
    {
        And,
        Or
    }
}
