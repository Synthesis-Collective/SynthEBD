using GongSolutions.Wpf.DragDrop;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SynthEBD;

[DebuggerDisplay("{DebuggerString}")]
public class VM_SubgroupPlaceHolder : VM, ICloneable
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly RecordPathParser _recordPathParser;
    private readonly UpdateHandler _updateHandler;
    private readonly Logger _logger;
    private readonly Factory _selfFactory;
    public delegate VM_SubgroupPlaceHolder Factory(AssetPack.Subgroup associatedModel, VM_SubgroupPlaceHolder parentSubgroup, VM_AssetPack parentAssetPack, ObservableCollection<VM_SubgroupPlaceHolder> parentCollection);
    public VM_SubgroupPlaceHolder(AssetPack.Subgroup associatedModel, VM_SubgroupPlaceHolder parentSubgroup, VM_AssetPack parentAssetPack, ObservableCollection<VM_SubgroupPlaceHolder> parentCollection, IEnvironmentStateProvider environmentProvider, RecordPathParser recordPathParser, UpdateHandler updateHandler, Logger logger, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _recordPathParser = recordPathParser;
        _updateHandler = updateHandler;
        _logger = logger;
        _selfFactory = selfFactory;
        AssociatedModel = associatedModel;
        ID = AssociatedModel.ID;
        Name = AssociatedModel.Name;
        ParentSubgroup = parentSubgroup;
        ParentAssetPack = parentAssetPack;
        ParentCollection = parentCollection;

        if (Name.IsNullOrWhitespace())
        {
            Name = "New Subgroup";
            associatedModel.Name = Name;
        }

        if (ID.IsNullOrWhitespace())
        {
            AutoGenerateID(false, 0);
            associatedModel.ID = ID;
        }

        foreach(var subgroup in AssociatedModel.Subgroups)
        {
            Subgroups.Add(_selfFactory(subgroup, this, ParentAssetPack, Subgroups));
        }

        this.WhenAnyValue(x => x.AssociatedViewModel.ID).Subscribe(y => ID = y).DisposeWith(this);
        this.WhenAnyValue(x => x.AssociatedViewModel.Name).Subscribe(y => Name = y).DisposeWith(this);

        DeleteMe = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                //remove this subgroup from the Required/Excluded lists of other subgroups
                foreach (var subgroup in ParentAssetPack.Subgroups)
                {
                    DeleteFromRequiredExcludedSubgroupsLists(subgroup, ID);
                }
                ParentCollection.Remove(this);
            }
        );

        AddSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                var newSubgroup = new AssetPack.Subgroup();
                var newPlaceHolder = _selfFactory(newSubgroup, this, ParentAssetPack, Subgroups);
                Subgroups.Add(newPlaceHolder);
            });
    }

    public AssetPack.Subgroup AssociatedModel { get; set; } = new();
    public VM_Subgroup? AssociatedViewModel { get; set; }
    public string ID { get; set; }
    public string Name { get; set; }
    public ObservableCollection<VM_SubgroupPlaceHolder> Subgroups { get; set; } = new();
    public VM_SubgroupPlaceHolder? ParentSubgroup { get; set; } = null;
    public VM_AssetPack ParentAssetPack { get; set; } = null;
    public ObservableCollection<VM_SubgroupPlaceHolder> ParentCollection { get; set; }
    public ObservableCollection<ImagePreviewHandler.ImagePathWithSource> ImagePaths { get; set; } = new();
    public int ImagePreviewRefreshTrigger { get; set; } = 0; // this seems ridiculous but actually works well
    public RelayCommand DeleteMe { get; }
    public RelayCommand AddSubgroup { get; }
    public string DebuggerString
    {
        get
        {
            return ID + ": " + Name;
        }
    }

    public void SaveToModel()
    {
        AssociatedModel.Subgroups.Clear();
        foreach (var subgroup in Subgroups)
        {
            subgroup.SaveToModel();
            AssociatedModel.Subgroups.Add(subgroup.AssociatedModel);
        }
    }

    public void Refresh()
    {
        ID = AssociatedModel.ID;
        foreach (var sg in Subgroups)
        {
            sg.Refresh();
        }
    }

    public static VM_SubgroupPlaceHolder GetSubgroupByID(ObservableCollection<VM_SubgroupPlaceHolder> subgroups, string id)
    {
        foreach (var sg in subgroups)
        {
            if (sg.ID == id) { return sg; }
            else
            {
                var candidate = GetSubgroupByID(sg.Subgroups, id);
                if (candidate != null) { return candidate; }
            }
        }
        return null;
    }

    public void AutoGenerateSubgroupIDs()
    {
        ClearSubgroupIDs();
        foreach (var subgroup in Subgroups)
        {
            subgroup.AutoGenerateID(true, 0);
        }
    }

    public void AutoGenerateID(bool recursive, int skipLayers)
    {
        if (skipLayers <= 0)
        {
            List<string> ids = new();
            List<VM_SubgroupPlaceHolder> parents = GetParents();

            parents.Reverse();
            for (int i = 0; i < parents.Count; i++)
            {
                var parent = parents[i];
                if (parent.ID.IsNullOrWhitespace())
                {
                    ids.Add("New");
                }
                else
                {
                    var splitID = parent.ID.Split('.');
                    ids.Add(splitID.Last());
                }
            }

            //get abbreviation for current subgroup name
            if (Name.IsNullOrWhitespace())
            {
                ids.Add("New");
            }
            else
            {
                var words = System.Text.RegularExpressions.Regex.Split(Name, @"\s+").Where(s => s != string.Empty).ToList();
                if (words.Count == 1 && words.First().Length <= 3)
                {
                    ids.Add(Name);
                }
                else
                {
                    var chars = new List<string>();
                    foreach (var word in words)
                    {
                        if (CanSplitByLettersAndNumbers(word, out var letterAndNumbers))
                        {
                            chars.Add(letterAndNumbers);
                        }
                        else
                        {
                            var candidate = Regex.Replace(word, "[^a-zA-Z0-9]", String.Empty); // remove non-alphanumeric
                            if (!candidate.IsNullOrEmpty())
                            {
                                if (candidate.IsNumeric())
                                {
                                    chars.Add(candidate);
                                }
                                else
                                {
                                    chars.Add(candidate.First().ToString());
                                }
                            }
                        }
                    }
                    ids.Add(string.Join("", chars));
                }
            }

            ID = string.Join('.', ids);
            ID = TrimTrailingNonAlphaNumeric(ID);

            EnumerateID();
        }
        skipLayers--;
        if (recursive)
        {
            foreach (var subgroup in Subgroups)
            {
                subgroup.AutoGenerateID(recursive, skipLayers);
            }
        }

        string oldID = AssociatedModel.ID;
        AssociatedModel.ID = ID;

        //update required and excluded subgroups in OTHER subgroups to keep up with this new ID
        foreach (var subgroup in ParentAssetPack.Subgroups)
        {
            UpdateRequiredExcludedSubgroupIDs(subgroup, oldID, ID);
        }
    }

    public static void UpdateRequiredExcludedSubgroupIDs(VM_SubgroupPlaceHolder subgroup, string oldSubgroupID, string updatedSubgroupID)
    {
        var required = subgroup.AssociatedModel.RequiredSubgroups.ToList();
        for (int i = 0; i < required.Count; i++)
        {
            if (required[i] == oldSubgroupID)
            {
                required[i] = updatedSubgroupID;
            }
        }
        subgroup.AssociatedModel.RequiredSubgroups = required.ToHashSet();

        var excluded = subgroup.AssociatedModel.ExcludedSubgroups.ToList();
        for (int i = 0; i < excluded.Count; i++)
        {
            if (excluded[i] == oldSubgroupID)
            {
                excluded[i] = updatedSubgroupID;
            }
        }
        subgroup.AssociatedModel.ExcludedSubgroups = excluded.ToHashSet();

        foreach (var sg in subgroup.Subgroups)
        {
            UpdateRequiredExcludedSubgroupIDs(sg, oldSubgroupID, updatedSubgroupID);
        }
    }

    private static void DeleteFromRequiredExcludedSubgroupsLists(VM_SubgroupPlaceHolder subgroup, string deleteID)
    {
        if (subgroup.AssociatedModel.RequiredSubgroups.Contains(deleteID))
        {
            subgroup.AssociatedModel.RequiredSubgroups.Remove(deleteID);
        }

        if (subgroup.AssociatedModel.ExcludedSubgroups.Contains(deleteID))
        {
            subgroup.AssociatedModel.ExcludedSubgroups.Remove(deleteID);
        }

        foreach (var sg in subgroup.Subgroups)
        {
            DeleteFromRequiredExcludedSubgroupsLists(sg, deleteID);
        }
    }

    public static string TrimTrailingNonAlphaNumeric(string s)
    {
        while (s != string.Empty && !char.IsLetterOrDigit(s.Last()))
        {
            s = s.Substring(0, s.Length - 1);
        }
        return s;
    }

    public void EnumerateID()
    {
        var newID = ID;
        ID = string.Empty;
        bool isUniqueID = false;
        HashSet<string> previousSplitNames = new();
        int count = 0; // algorithm can hang if there is two subgroups exist whose IDs should be swapped. Snap out if hang is detected
        string appendStr = string.Empty;
        while (!isUniqueID)
        {
            if (ParentAssetPack.ContainsSubgroupID(newID))
            {
                var lastID = newID.Split('.').Last();
                if (lastID == null)
                {
                    newID = "New"; // don't think this should ever happen...
                }
                if (count < 100) // seems like a reasonable number of iterations
                {
                    int appendCount = 1;
                    while (ParentAssetPack.ContainsSubgroupID(newID))
                    {
                        if (appendStr != string.Empty)
                        {
                            newID = newID.Remove(newID.Length - appendStr.Length, appendStr.Length);
                        }

                        appendStr = "_" + appendCount.ToString();
                        newID = newID.Replace(lastID, lastID + appendStr);
                        appendCount++;
                        if (appendCount > 100)
                        {
                            _logger.LogError("Could not auto-generate ID for subgroup " + newID);
                            break;
                        }
                    }
                }
                else if (lastID.Any() && CanSplitByLettersAndNumbers(newID, out string renamed1))
                {
                    newID = newID.Replace(lastID, renamed1);
                }
                else if (lastID.Any() && CanExtendWordSplit(lastID, Name, previousSplitNames, ParentAssetPack, out string renamed2))
                {
                    newID = newID.Replace(lastID, renamed2);
                }
                else if (lastID.Any() && lastID.Length < Name.Length)
                {
                    newID = newID.Replace(lastID, Name.Substring(0, lastID.Length + 1));
                }
                else
                {
                    newID = IncrementID(newID);
                }

                newID = MiscFunctions.MakeXMLtagCompatible(newID);
                count++;
            }
            else
            {
                ID = newID;
                isUniqueID = true;
            }
        }
    }

    public static bool CanExtendWordSplit(string s, string name, HashSet<string> previousNames, VM_AssetPack assetPack, out string renamed)
    {
        renamed = s;

        var split = name.Split();

        if (split.Length == 1)
        {
            return false;
        }

        string prefix = "";
        for (int i = 0; i < split.Length - 1; i++)
        {
            var candidate = Regex.Replace(split[i], "[^a-zA-Z0-9]", String.Empty);
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }
            else
            {
                prefix += candidate.First();
            }
        }

        string lastWord = Regex.Replace(split.Last(), "[^a-zA-Z0-9]", String.Empty);
        string suffix = lastWord.First().ToString();

        string trial = prefix + suffix;

        while (suffix.Length <= lastWord.Length && assetPack.ContainsSubgroupID(trial))
        {
            suffix = lastWord.Substring(0, suffix.Length + 1);
            trial = prefix + suffix;
        }

        if (assetPack.ContainsSubgroupID(trial))
        {
            return false;
        }
        else
        {
            renamed = trial;
            return true;
        }
    }

    public bool CanSplitByLettersAndNumbers(string s, out string renamed)
    {
        if (s.IsNumeric()) { renamed = s; return false; }

        var trimNumbers = s.TrimEnd(" 1234567890".ToCharArray());
        if (trimNumbers == s)
        {
            renamed = s;
            return false;
        }

        var numbers = s.Substring(trimNumbers.Length, s.Length - trimNumbers.Length);
        var letters = trimNumbers.First().ToString();
        renamed = trimNumbers.First() + numbers;

        if (renamed == s)
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    public static string IncrementID(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "New";
        }
        if (id.Length < 2)
        {
            return id + "_1";
        }
        if (id.Contains('_'))
        {
            var split = id.Split('_');
            if (int.TryParse(split.Last(), out int index))
            {
                split[split.Length - 1] = (index + 1).ToString();
                return String.Join('_', split);
            }
            else
            {
                return id + "_1";
            }
        }
        return id + "_1";
    }

    public bool IsParentOf(VM_SubgroupPlaceHolder candidateChild)
    {
        foreach (var subgroup in Subgroups)
        {
            if (candidateChild == subgroup)
            { return true; }
            else if (subgroup.IsParentOf(candidateChild))
            {
                return true;
            }
        }
        return false;
    }
    public List<VM_SubgroupPlaceHolder> GetParents()
    {
        List<VM_SubgroupPlaceHolder> parents = new();
        GetParents(parents);
        return parents;
    }

    public void GetParents(List<VM_SubgroupPlaceHolder> parents)
    {
        if (ParentSubgroup is not null)
        {
            parents.Add(ParentSubgroup);
            ParentSubgroup.GetParents(parents);
        }
    }

    public List<VM_SubgroupPlaceHolder> GetChildren()
    {
        List<VM_SubgroupPlaceHolder> children = new();
        GetChildren(children);
        return children;
    }

    public void GetChildren(List<VM_SubgroupPlaceHolder> children)
    {
        foreach (var subgroup in Subgroups)
        {
            children.Add(subgroup);
            subgroup.GetChildren(children);
        }
    }

    public void ClearID(bool recursive)
    {
        ID = String.Empty;
        if (recursive)
        {
            foreach (var subgroup in Subgroups)
            {
                subgroup.ClearID(recursive);
            }
        }
    }

    public void ClearSubgroupIDs()
    {
        foreach (var subgroup in Subgroups)
        {
            subgroup.ClearID(true);
        }
    }

    public bool ContainsID(string id)
    {
        if (ID == id) { return true; }
        foreach (var subgroup in Subgroups)
        {
            if (subgroup.ContainsID(id))
            {
                return true;
            }
        }
        return false;
    }

    public void GetDDSPaths()
    {
        ImagePaths.Clear();
        GetDDSPaths(ImagePaths);
    }

    private void GetDDSPaths(ObservableCollection<ImagePreviewHandler.ImagePathWithSource> paths)
    {
        HashSet<string> ddsPaths = new HashSet<string>();
        if (ParentAssetPack.SelectedPlaceHolder == this && AssociatedViewModel != null)
        {
            ddsPaths = AssociatedViewModel.PathsMenu.Paths
                .Where(x => x.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(_environmentProvider.DataFolderPath, x.Source)))
                .Select(x => x.Source)
                .Select(x => System.IO.Path.Combine(_environmentProvider.DataFolderPath, x))
                .ToHashSet();
        }
        else
        {
            ddsPaths = AssociatedModel.Paths.Where(x => x.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(_environmentProvider.DataFolderPath, x.Source)))
                .Select(x => x.Source)
                .Select(x => System.IO.Path.Combine(_environmentProvider.DataFolderPath, x))
                .ToHashSet() ?? new HashSet<string>();
        }

        foreach (var path in ddsPaths)
        {
            var imagePathWithSource = new ImagePreviewHandler.ImagePathWithSource(path, this);
            if (!paths.Contains(imagePathWithSource))
            {
                paths.Add(imagePathWithSource);
            }
        }

        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDDSPaths(paths);
        }
    }

    public bool VersionUpdate(Version version, UpdateMode updateAction)
    {
        if (version == Version.v090)
        {
            foreach (var path in AssociatedModel.Paths)
            {
                string lastClass = path.Destination.Split('.').Last();
                bool pathExists = VM_FilePathReplacement.DestinationPathExists(path.Destination, ParentAssetPack.RecordTemplateLinkCache, ParentAssetPack.AllReferenceNPCs, _recordPathParser, _logger);
                if (!pathExists && _updateHandler.V09PathReplacements.ContainsKey(lastClass))
                {
                    switch(updateAction)
                    {
                        case UpdateMode.Check:
                            return true;
                        case UpdateMode.Perform:
                            path.Destination = MiscFunctions.ReplaceLastOccurrence(path.Destination, lastClass, _updateHandler.V09PathReplacements[lastClass]);
                            break;
                    }
                }
            }
        }

        foreach (var subgroup in Subgroups)
        {
            bool bUpdate = subgroup.VersionUpdate(version, updateAction);
            if (bUpdate && updateAction == UpdateMode.Check)
            {
                return true;
            }
        }

        return false;
    }

    public void GetDisabledSubgroups(List<string> disabledSubgroups)
    {
        if (!AssociatedModel.Enabled)
        {
            disabledSubgroups.Add(GetReportString(false));
        }
        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDisabledSubgroups(disabledSubgroups);
        }
    }

    public string GetReportString(bool shortName)
    {
        if (shortName)
        {
            return ID + ": " + Name;
        }
        else
        {
            List<VM_SubgroupPlaceHolder> parents = new();
            GetParents(parents);
            var names = parents.Select(x => x.Name);
            var nameStr = string.Join("\\", names);
            return ID + ": " + nameStr + "\\" + Name;
        }
    }

    public List<string> GetRulesSummary()
    {
        List<string> rulesSummary = new();
        string tmpReport = "";
        if (_logger.GetRaceLogString("Allowed", AssociatedModel.AllowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Allowed", AssociatedModel.AllowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceLogString("Disallowed", AssociatedModel.DisallowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Disallowed", AssociatedModel.DisallowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Allowed", AssociatedModel.AllowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Disallowed", AssociatedModel.DisallowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (!AssociatedModel.AllowUnique) { rulesSummary.Add("Unique NPCs: Disallowed"); }
        if (!AssociatedModel.AllowNonUnique) { rulesSummary.Add("Generic NPCs: Disallowed"); }
        if (AssociatedModel.ProbabilityWeighting != 1) { rulesSummary.Add("Probability Weighting: " + AssociatedModel.ProbabilityWeighting.ToString()); }
        if (AssociatedModel.WeightRange.Lower != 0 || AssociatedModel.WeightRange.Upper != 100) { rulesSummary.Add("Weight Range: " + AssociatedModel.WeightRange.Lower.ToString() + " to " + AssociatedModel.WeightRange.Upper.ToString()); }


        if (rulesSummary.Any())
        {
            rulesSummary.Insert(0, ID + ": " + Name);
            rulesSummary.Add("");
        }

        foreach (var subgroup in Subgroups)
        {
            rulesSummary.AddRange(subgroup.GetRulesSummary());
        }

        return rulesSummary;
    }

    public object Clone()
    {
        return Clone(ParentAssetPack, ParentCollection);
    }
    public VM_SubgroupPlaceHolder Clone(VM_AssetPack parentAssetPack, ObservableCollection<VM_SubgroupPlaceHolder> parentCollection)
    {
        if (AssociatedViewModel != null)
        {
           AssociatedModel = AssociatedViewModel.DumpViewModelToModel();
        }
        SaveToModel();
        var clonedModel = JSONhandler<AssetPack.Subgroup>.CloneViaJSON(AssociatedModel);
        return _selfFactory(clonedModel, this, parentAssetPack, parentCollection);
    }
}
