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
        public VM_SubgroupLinker(VM_AssetPack assetPack, VM_Subgroup subgroup, Window_SubgroupLinker window)
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
                    LinkThisToFn();
                    window.Close();
                }
            );

            UnlinkThisFrom = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    UnlinkThisFromFn();
                    window.Close();
                }
            );

            LinkToThis = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    LinkToThisFn();
                    window.Close();
                }
            );

            UnlinkFromThis = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    UnlinkFromThisFn();
                    window.Close();
                }
            );

            LinkReciprocally = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    LinkToThisFn();
                    LinkThisToFn();
                    window.Close();
                }
            );

            LinkWholeGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    LinkWholeGroupFn();
                    window.Close();
                }
            );

            UnlinkReciprocally = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    UnlinkThisFromFn();
                    UnlinkFromThisFn();
                    window.Close();
                }
            );

            UnlinkWholeGroup = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => {
                    UnlinkWholeGroupFn();
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

        private VM_AssetPack _targetAssetPack;
        private VM_Subgroup _targetSubgroup;
        private int _topLevelIndex = -1;
        public string IdToMatch { get; set; } = string.Empty;
        public string NameToMatch { get; set; } = string.Empty;
        public AndOr AndOrSelection { get; set; } = AndOr.Or;
        public bool IDcaseSensitive { get; set; } = false;
        public bool NameCaseSensitive { get; set; } = false;
        public bool IDallowPartial { get; set; } = false;
        public bool NameAllowPartial { get; set; } = false;

        public ObservableCollection<VM_SelectableSubgroupShell> CollectedSubgroups { get; set; } = new(); // does not include _targetSubgroup

        public RelayCommand LinkThisTo { get; }
        public RelayCommand LinkToThis { get; }
        public RelayCommand LinkReciprocally { get; }
        public RelayCommand LinkWholeGroup { get; }
        public RelayCommand UnlinkThisFrom { get; }
        public RelayCommand UnlinkFromThis { get; }
        public RelayCommand UnlinkReciprocally { get; }
        public RelayCommand UnlinkWholeGroup { get; }
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

        private void CollectMatchingSubgroups(VM_SubgroupPlaceHolder subgroup)
        {
            if (bSubgroupMatches(subgroup.AssociatedModel))
            {
                CollectedSubgroups.Add(new(subgroup, true, SubgroupLabelFormat.IDandDeepName));
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
                if (subgroup.ID == _targetSubgroup.ID || IndexContainsThisSubgroup(subgroup.Subgroups))
                {
                    _topLevelIndex = i;
                    return true;
                }
            }
            return false;
        }
        private bool IndexContainsThisSubgroup(IEnumerable<VM_SubgroupPlaceHolder> subgroups)
        {
            if (subgroups.Select(x => x.ID).Contains(_targetSubgroup.ID))
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
        
        private void LinkThisToFn()
        {
            foreach (var sg in CollectedSubgroups.Where(x => x.IsSelected).ToArray())
            {
                if (!_targetSubgroup.RequiredSubgroups.Select(x => x.AssociatedModel.ID).Contains(sg.Subgroup.ID) && _targetAssetPack.TryGetSubgroupByID(sg.Subgroup.ID, out var placeHolder))
                {
                    _targetSubgroup.RequiredSubgroups.Add(placeHolder);
                }
            }
        }

        private void LinkToThisFn()
        {
            foreach (var sg in CollectedSubgroups.Where(x => x.IsSelected).ToArray())
            {
                if (!sg.Subgroup.AssociatedModel.RequiredSubgroups.Contains(_targetSubgroup.ID))
                {
                    sg.Subgroup.AssociatedModel.RequiredSubgroups.Add(_targetSubgroup.ID);
                }
            }
        }

        private void LinkWholeGroupFn()
        {
            var wholeSet = new List<VM_SubgroupPlaceHolder>();
            wholeSet.Add(_targetSubgroup.AssociatedPlaceHolder);
            wholeSet.AddRange(CollectedSubgroups.Where(x => x.IsSelected).Select(x => x.Subgroup));

            foreach (var sg in wholeSet)
            {
                foreach (var sg2 in wholeSet)
                {
                    // don't link if subgroups are at the same top level index
                    if (sg.GetTopLevelIndex() == sg2.GetTopLevelIndex())
                    {
                        continue;
                    }
                    //if sg is the current view model, operate in the view model space
                    if (sg.ID == _targetSubgroup.ID)
                    {
                        if (sg2.ID != sg.ID && !_targetSubgroup.RequiredSubgroups.Select(x => x.AssociatedModel.ID).Contains(sg2.ID) && _targetAssetPack.TryGetSubgroupByID(sg2.ID, out var placeHolder))
                        {
                            _targetSubgroup.RequiredSubgroups.Add(placeHolder);
                        }
                    }
                    else // otherwise operate in the model space
                    {
                        if (sg2.ID != sg.ID && !sg.AssociatedModel.RequiredSubgroups.Contains(sg2.ID))
                        {
                            sg.AssociatedModel.RequiredSubgroups.Add(sg2.ID);
                        }
                    }
                }
            }
        }

        private void UnlinkThisFromFn()
        {
            _targetSubgroup.RequiredSubgroups.RemoveWhere(x => CollectedSubgroups.Where(y => y.IsSelected).Select(y => y.Subgroup.ID).Contains(x.ID));
        }

        private void UnlinkFromThisFn()
        {
            foreach (var sg in CollectedSubgroups.Where(x => x.IsSelected).ToArray())
            {
                sg.Subgroup.AssociatedModel.RequiredSubgroups.RemoveWhere(x => x == _targetSubgroup.ID);
            }
        }
        
        private void UnlinkWholeGroupFn()
        {
            var wholeSet = new List<VM_SubgroupPlaceHolder>();
            wholeSet.Add(_targetSubgroup.AssociatedPlaceHolder);
            wholeSet.AddRange(CollectedSubgroups.Where(x => x.IsSelected).Select(x => x.Subgroup));

            foreach (var sg in wholeSet)
            {
                foreach (var sg2 in wholeSet)
                {
                    //if sg is the current view model, operate in the view model space
                    if (sg.ID == _targetSubgroup.ID)
                    {
                        if (sg2.ID != sg.ID && _targetSubgroup.RequiredSubgroups.Select(x => x.AssociatedModel.ID).Contains(sg2.ID) && _targetAssetPack.TryGetSubgroupByID(sg2.ID, out var placeHolder))
                        {
                            _targetSubgroup.RequiredSubgroups.Remove(placeHolder);
                        }
                    }
                    else // otherwise operate in the model space
                    {
                        if (sg2.ID != sg.ID && sg.AssociatedModel.RequiredSubgroups.Contains(sg2.ID))
                        {
                            sg.AssociatedModel.RequiredSubgroups.Remove(sg2.ID);
                        }
                    }
                }
            }
        }
    }

    public enum AndOr
    {
        And,
        Or
    }
}
