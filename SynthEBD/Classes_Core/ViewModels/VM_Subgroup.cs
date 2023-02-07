using System.Collections.ObjectModel;
using System.ComponentModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using ReactiveUI;
using GongSolutions.Wpf.DragDrop;
using DynamicData.Binding;
using System.Text.RegularExpressions;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class VM_Subgroup : VM, ICloneable, IDropTarget, IHasSubgroupViewModels
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly VM_SettingsOBody _oBody;
    private readonly VM_NPCAttributeCreator _attributeCreator;
    private readonly Factory _selfFactory;
    private readonly VM_FilePathReplacementMenu.Factory _filePathReplacementMenuFactory;
    private readonly VM_FilePathReplacement.Factory _filePathReplacementFactory;
    private readonly VM_BodyShapeDescriptorSelectionMenu.Factory _descriptorSelectionFactory;
    private readonly UpdateHandler _updateHandler;

    public delegate VM_Subgroup Factory(
        ObservableCollection<VM_RaceGrouping> raceGroupingVMs,
        ObservableCollection<VM_Subgroup> parentCollection,
        VM_AssetPack parentAssetPack,
        VM_Subgroup parentSubgroup,
        bool setExplicitReferenceNPC);
    
    public VM_Subgroup(IEnvironmentStateProvider environmentProvider,
        Logger logger,
        SynthEBDPaths paths,
        ObservableCollection<VM_RaceGrouping> raceGroupingVMs,
        ObservableCollection<VM_Subgroup> parentCollection,
        VM_AssetPack parentAssetPack, 
        VM_Subgroup parentSubgroup,
        bool setExplicitReferenceNPC,
        VM_SettingsOBody oBody,
        VM_NPCAttributeCreator attributeCreator,
        Factory selfFactory,
        VM_FilePathReplacementMenu.Factory filePathReplacementMenuFactory,
        VM_FilePathReplacement.Factory filePathReplacementFactory,
        VM_BodyShapeDescriptorSelectionMenu.Factory descriptorSelectionFactory,
        UpdateHandler updateHandler)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
        _oBody = oBody;
        _attributeCreator = attributeCreator;
        _selfFactory = selfFactory;
        _filePathReplacementMenuFactory = filePathReplacementMenuFactory;
        _filePathReplacementFactory = filePathReplacementFactory;
        _descriptorSelectionFactory = descriptorSelectionFactory;
        _updateHandler = updateHandler;

        SetExplicitReferenceNPC = setExplicitReferenceNPC;
        ParentAssetPack = parentAssetPack;
        SubscribedRaceGroupings = ParentAssetPack.RaceGroupingEditor.RaceGroupings;

        AllowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);
        DisallowedRaceGroupings = new VM_RaceGroupingCheckboxList(SubscribedRaceGroupings);

        RefreshBodyGenDescriptors();
        this.WhenAnyValue(x => x.ParentAssetPack.TrackedBodyGenConfig).Subscribe(_ => RefreshBodyGenDescriptors()).DisposeWith(this);
        AllowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.All);
        DisallowedBodySlideDescriptors = _descriptorSelectionFactory(oBody.DescriptorUI, SubscribedRaceGroupings, parentAssetPack, true, DescriptorMatchMode.Any);

        _environmentProvider.WhenAnyValue(x => x.LinkCache)
            .Subscribe(x => LinkCache = x)
            .DisposeWith(this);
        
        //UI-related
        ParentCollection = parentCollection;
        ParentSubgroup = parentSubgroup;
        RequiredSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required)).DisposeWith(this);
        ExcludedSubgroups.ToObservableChangeSet().Subscribe(x => RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded)).DisposeWith(this);

        // must be set after Parent Asset Pack
        if (SetExplicitReferenceNPC)
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, LinkCache);
        }
        else
        {
            PathsMenu = _filePathReplacementMenuFactory(this, SetExplicitReferenceNPC, parentAssetPack.RecordTemplateLinkCache);
            parentAssetPack.WhenAnyValue(x => x.RecordTemplateLinkCache).Subscribe(x => PathsMenu.ReferenceLinkCache = parentAssetPack.RecordTemplateLinkCache).DisposeWith(this);
        }

        PathsMenu.Paths.ToObservableChangeSet().Subscribe(x => GetDDSPaths(ImagePaths)).DisposeWith(this);
        this.WhenAnyValue(x => x.ParentAssetPack, x => x.ParentSubgroup).Subscribe(x => GetDDSPaths(ImagePaths)).DisposeWith(this);

        AutoGenerateID(false, 0);

        AutoGenerateIDcommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AutoGenerateID(false, 0)
        );

        AutoGenerateID_Children_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AutoGenerateID(true, 1)
        );

        AutoGenerateID_All_Command = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentAssetPack.AutoGenerateSubgroupIDs()
        );

        AddAllowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AllowedAttributes.Add(_attributeCreator.CreateNewFromUI(AllowedAttributes, true, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddDisallowedAttribute = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => DisallowedAttributes.Add(_attributeCreator.CreateNewFromUI(DisallowedAttributes, false, null, parentAssetPack.AttributeGroupMenu.Groups))
        );

        AddNPCKeyword = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AddKeywords.Add(new VM_CollectionMemberString("", AddKeywords))
        );

        AddPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => PathsMenu.Paths.Add(filePathReplacementFactory(PathsMenu))
        );

        AddSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => Subgroups.Add(selfFactory(raceGroupingVMs, Subgroups, ParentAssetPack, this, setExplicitReferenceNPC))
        );

        DeleteMe = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => ParentCollection.Remove(this)
        );

        DeleteRequiredSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute:  x => { RequiredSubgroups.Remove((VM_Subgroup)x); RefreshListBoxLabel(RequiredSubgroups, SubgroupListBox.Required); }
        );

        DeleteExcludedSubgroup = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: x => { ExcludedSubgroups.Remove((VM_Subgroup)x); RefreshListBoxLabel(ExcludedSubgroups, SubgroupListBox.Excluded); }
        );
    }

    public string ID { get; set; } = "";
    public string Name { get; set; } = "New";
    public bool Enabled { get; set; } = true;
    public bool DistributionEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
    public ObservableCollection<FormKey> AllowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList AllowedRaceGroupings { get; set; }
    public ObservableCollection<FormKey> DisallowedRaces { get; set; } = new();
    public VM_RaceGroupingCheckboxList DisallowedRaceGroupings { get; set; }
    public ObservableCollection<VM_NPCAttribute> AllowedAttributes { get; set; } = new();
    public ObservableCollection<VM_NPCAttribute> DisallowedAttributes { get; set; } = new();
    public bool AllowUnique { get; set; } = true;
    public bool AllowNonUnique { get; set; } = true;
    public ObservableCollection<VM_Subgroup> RequiredSubgroups { get; set; } = new();
    public ObservableCollection<VM_Subgroup> ExcludedSubgroups { get; set; } = new();
    public ObservableCollection<VM_CollectionMemberString> AddKeywords { get; set; } = new();
    public double ProbabilityWeighting { get; set; } = 1;
    public VM_FilePathReplacementMenu PathsMenu { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodyGenDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu AllowedBodySlideDescriptors { get; set; }
    public VM_BodyShapeDescriptorSelectionMenu DisallowedBodySlideDescriptors { get; set; }
    public NPCWeightRange WeightRange { get; set; } = new();
    public ObservableCollection<VM_Subgroup> Subgroups { get; set; } = new();

    //UI-related
    public ILinkCache LinkCache { get; private set; }
    public IEnumerable<Type> RacePickerFormKeys { get; set; } = typeof(IRaceGetter).AsEnumerable();

    public string TopLevelSubgroupID { get; set; }
    public RelayCommand AutoGenerateIDcommand { get; }
    public RelayCommand AutoGenerateID_Children_Command { get; }
    public RelayCommand AutoGenerateID_All_Command { get; }
    public RelayCommand AddAllowedAttribute { get; }
    public RelayCommand AddDisallowedAttribute { get; }
    public RelayCommand AddNPCKeyword { get; }
    public RelayCommand AddPath { get; }
    public RelayCommand AddSubgroup { get; }
    public RelayCommand DeleteMe { get; }
    public RelayCommand DeleteRequiredSubgroup { get; }
    public RelayCommand DeleteExcludedSubgroup { get; }
    public bool SetExplicitReferenceNPC { get; set; }
    public HashSet<string> RequiredSubgroupIDs { get; set; } = new(); // temporary placeholder for RequiredSubgroups until all subgroups are loaded in
    public HashSet<string> ExcludedSubgroupIDs { get; set; } = new(); // temporary placeholder for ExcludedSubgroups until all subgroups are loaded in
    public string RequiredSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";
    public string ExcludedSubgroupsLabel { get; set; } = "Drag subgroups here from the tree view";

    public ObservableCollection<VM_Subgroup> ParentCollection { get; set; }
    public VM_AssetPack ParentAssetPack { get; set; }
    public VM_Subgroup ParentSubgroup { get; set; }
    public ObservableCollection<VM_RaceGrouping> SubscribedRaceGroupings { get; set; }
    public ObservableCollection<ImagePreviewHandler.ImagePathWithSource> ImagePaths { get; set; } = new();

    public void CopyInViewModelFromModel(
        AssetPack.Subgroup model)
    {
        ID = model.ID;
        Name = model.Name;
        Enabled = model.Enabled;
        DistributionEnabled = model.DistributionEnabled;
        Notes = model.Notes;
        AllowedRaces = new ObservableCollection<FormKey>(model.AllowedRaces);
        AllowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.AllowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        DisallowedRaces = new ObservableCollection<FormKey>(model.DisallowedRaces);
        DisallowedRaceGroupings = VM_RaceGroupingCheckboxList.GetRaceGroupingsByLabel(model.DisallowedRaceGroupings, ParentAssetPack.RaceGroupingEditor.RaceGroupings);
        AllowedAttributes = _attributeCreator.GetViewModelsFromModels(model.AllowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, true, null);
        DisallowedAttributes = _attributeCreator.GetViewModelsFromModels(model.DisallowedAttributes, ParentAssetPack.AttributeGroupMenu.Groups, false, null);
        foreach (var x in DisallowedAttributes) { x.DisplayForceIfOption = false; }
        AllowUnique = model.AllowUnique;
        AllowNonUnique = model.AllowNonUnique;
        RequiredSubgroupIDs = model.RequiredSubgroups;
        RequiredSubgroups = new ObservableCollection<VM_Subgroup>();
        ExcludedSubgroupIDs = model.ExcludedSubgroups;
        ExcludedSubgroups = new ObservableCollection<VM_Subgroup>();
        AddKeywords = VM_CollectionMemberString.InitializeObservableCollectionFromICollection(model.AddKeywords);
        ProbabilityWeighting = model.ProbabilityWeighting;
        PathsMenu = VM_FilePathReplacementMenu.GetViewModelFromModels(model.Paths, this, SetExplicitReferenceNPC, _filePathReplacementMenuFactory, _filePathReplacementFactory);
        WeightRange = model.WeightRange;

        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodyGenDescriptors, ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, model.AllowedBodyGenMatchMode, _descriptorSelectionFactory);
            DisallowedBodyGenDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodyGenDescriptors, ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, model.DisallowedBodyGenMatchMode, _descriptorSelectionFactory);
        }

        AllowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.AllowedBodySlideDescriptors, _oBody.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, model.AllowedBodySlideMatchMode, _descriptorSelectionFactory);
        DisallowedBodySlideDescriptors = VM_BodyShapeDescriptorSelectionMenu.InitializeFromHashSet(model.DisallowedBodySlideDescriptors, _oBody.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, model.DisallowedBodySlideMatchMode, _descriptorSelectionFactory);

        foreach (var sg in model.Subgroups)
        {
            var subVm = _selfFactory(
                ParentAssetPack.RaceGroupingEditor.RaceGroupings,
                Subgroups,
                ParentAssetPack,
                this,
                SetExplicitReferenceNPC);
            subVm.CopyInViewModelFromModel(sg);
            Subgroups.Add(subVm);
        }

        //dds preview
        GetDDSPaths(ImagePaths);
    }
    public void RefreshListBoxLabel(ObservableCollection<VM_Subgroup> listSource, SubgroupListBox whichBox)
    {
        string label = "";
        if (listSource.Any())
        {
            label = "";
        }
        else
        {
            label = "Drag subgroups here from the tree view";
        }

        switch(whichBox)
        {
            case SubgroupListBox.Required: RequiredSubgroupsLabel = label; break;
            case SubgroupListBox.Excluded: ExcludedSubgroupsLabel = label; break;
        }
    }
    public enum SubgroupListBox
    {
        Required,
        Excluded
    }
    public void GetDDSPaths(ObservableCollection<ImagePreviewHandler.ImagePathWithSource> paths)
    {
        var ddsPaths = PathsMenu.Paths.Where(x => x.Source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(System.IO.Path.Combine(_environmentProvider.DataFolderPath, x.Source)))
            .Select(x => x.Source)
            .Select(x => System.IO.Path.Combine(_environmentProvider.DataFolderPath, x))
            .ToHashSet();
        foreach (var path in ddsPaths)
        {
            var imagePathWithSource = new ImagePreviewHandler.ImagePathWithSource(path, this);
            if (!paths.Contains(imagePathWithSource))
            {
                paths.Add(imagePathWithSource);
            }
        }
        //paths.UnionWith(ddsPaths.Select(x => System.IO.Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, x)));
        foreach (var subgroup in Subgroups)
        {
            subgroup.GetDDSPaths(paths);
        }
    }

    public void AutoGenerateID(bool recursive, int skipLayers)
    {
        if (skipLayers <= 0)
        {
            List<string> ids = new();
            List<VM_Subgroup> parents = new();
            GetParents(parents);

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

            //get abbreviate for current subgroup name
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
        while (!isUniqueID)
        {
            if (ParentAssetPack.ContainsSubgroupID(newID))
            {
                var lastID = newID.Split('.').Last();
                if (lastID == null)
                {
                    newID = "New"; // don't think this should ever happen...
                }
                if (count > 100) // seems like a reasonable number of iterations
                {
                    int appendCount = 1;
                    while (ParentAssetPack.ContainsSubgroupID(newID))
                    {
                        newID = newID.Replace(lastID, lastID + "_" + appendCount.ToString());
                        appendCount++;
                        if (appendCount > 100)
                        {
                            _logger.LogError("Could not auto-generated ID for subgroup " + newID);
                            break;
                        }
                    }
                    break;
                }
                else if (CanSplitByLettersAndNumbers(newID, out string renamed1))
                {
                    newID = newID.Replace(lastID, renamed1);
                }
                else if (CanExtendWordSplit(lastID, Name, previousSplitNames, ParentAssetPack, out string renamed2))
                {
                    newID = newID.Replace(lastID, renamed2);
                }
                else if (lastID.Length < Name.Length)
                {
                    newID = newID.Replace(lastID, Name.Substring(0, lastID.Length + 1));
                }
                else
                {
                    newID = IncrementID(newID);
                }
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

    public void GetParents(List<VM_Subgroup> parents)
    {
        if (ParentSubgroup is not null)
        {
            parents.Add(ParentSubgroup);
            ParentSubgroup.GetParents(parents);
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

    public void GetDisabledSubgroups(List<string> disabledSubgroups)
    {
        if (!Enabled)
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
            List<VM_Subgroup> parents = new();
            GetParents(parents);
            var names = parents.Select(x => x.Name);
            var nameStr = string.Join("\\", names);
            return ID + ": " + nameStr + "\\" + Name;
        }
    }

    public static AssetPack.Subgroup DumpViewModelToModel(VM_Subgroup viewModel)
    {
        var model = new AssetPack.Subgroup();

        model.ID = viewModel.ID;
        model.Name = viewModel.Name;
        model.Enabled = viewModel.Enabled;
        model.DistributionEnabled = viewModel.DistributionEnabled;
        model.Notes = viewModel.Notes;
        model.AllowedRaces = viewModel.AllowedRaces.ToHashSet();
        model.AllowedRaceGroupings = viewModel.AllowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.DisallowedRaces = viewModel.DisallowedRaces.ToHashSet();
        model.DisallowedRaceGroupings = viewModel.DisallowedRaceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToHashSet();
        model.AllowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.AllowedAttributes);
        model.DisallowedAttributes = VM_NPCAttribute.DumpViewModelsToModels(viewModel.DisallowedAttributes);
        model.AllowUnique = viewModel.AllowUnique;
        model.AllowNonUnique = viewModel.AllowNonUnique;
        model.RequiredSubgroups = viewModel.RequiredSubgroups.Select(x => x.ID).ToHashSet();
        model.ExcludedSubgroups = viewModel.ExcludedSubgroups.Select(x => x.ID).ToHashSet();
        model.AddKeywords = viewModel.AddKeywords.Select(x => x.Content).ToHashSet();
        model.ProbabilityWeighting = viewModel.ProbabilityWeighting;
        model.Paths = VM_FilePathReplacementMenu.DumpViewModelToModels(viewModel.PathsMenu);
        model.WeightRange = viewModel.WeightRange;

        model.AllowedBodyGenDescriptors = viewModel.AllowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.AllowedBodyGenMatchMode = viewModel.AllowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.All;
        model.DisallowedBodyGenDescriptors = viewModel.DisallowedBodyGenDescriptors?.DumpToHashSet() ?? null;
        model.DisallowedBodyGenMatchMode = viewModel.DisallowedBodyGenDescriptors?.MatchMode ?? DescriptorMatchMode.Any;
        model.AllowedBodySlideDescriptors = viewModel.AllowedBodySlideDescriptors.DumpToHashSet();
        model.AllowedBodySlideMatchMode = viewModel.AllowedBodySlideDescriptors.MatchMode;
        model.DisallowedBodySlideDescriptors = viewModel.DisallowedBodySlideDescriptors.DumpToHashSet();
        model.DisallowedBodySlideMatchMode = viewModel.DisallowedBodySlideDescriptors.MatchMode;

        foreach (var sg in viewModel.Subgroups)
        {
            model.Subgroups.Add(DumpViewModelToModel(sg));
        }

        return model;
    }

    public object Clone()
    {
        return Clone(ParentCollection);
    }

    public object Clone(ObservableCollection<VM_Subgroup> parentCollection)
    {
        var clone = _selfFactory(SubscribedRaceGroupings, parentCollection, ParentAssetPack, this, SetExplicitReferenceNPC);
        clone.AddKeywords = new ObservableCollection<VM_CollectionMemberString>(AddKeywords);
        clone.AllowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        foreach (var at in AllowedAttributes) { clone.AllowedAttributes.Add(at.Clone(clone.AllowedAttributes)); }
        clone.DisallowedAttributes = new ObservableCollection<VM_NPCAttribute>();
        foreach (var at in DisallowedAttributes) { clone.DisallowedAttributes.Add(at.Clone(clone.DisallowedAttributes)); }
        if (AllowedBodyGenDescriptors is not null)
        {
            clone.AllowedBodyGenDescriptors = AllowedBodyGenDescriptors.Clone();
        }
        if (DisallowedBodyGenDescriptors is not null)
        {
            clone.DisallowedBodyGenDescriptors = DisallowedBodyGenDescriptors.Clone();
        }
        clone.AllowedBodySlideDescriptors = AllowedBodySlideDescriptors.Clone();
        clone.DisallowedBodySlideDescriptors = DisallowedBodySlideDescriptors.Clone();
        clone.AllowedRaceGroupings = AllowedRaceGroupings.Clone();
        clone.DisallowedRaceGroupings = DisallowedRaceGroupings.Clone();
        clone.AllowedRaces = new ObservableCollection<FormKey>(AllowedRaces);
        clone.DisallowedRaces = new ObservableCollection<FormKey>(DisallowedRaces);
        clone.AllowUnique = AllowUnique;
        clone.AllowNonUnique = AllowNonUnique;
        clone.DistributionEnabled = DistributionEnabled;
        clone.ID = ID;
        clone.Name = Name;
        clone.RequiredSubgroupIDs = new HashSet<string>(RequiredSubgroupIDs);
        clone.ExcludedSubgroupIDs = new HashSet<string>(ExcludedSubgroupIDs);
        clone.RequiredSubgroups = new ObservableCollection<VM_Subgroup>(RequiredSubgroups);
        clone.ExcludedSubgroups = new ObservableCollection<VM_Subgroup>(ExcludedSubgroups);
        clone.WeightRange = WeightRange.Clone();
        clone.ProbabilityWeighting = ProbabilityWeighting;
        clone.PathsMenu = PathsMenu.Clone();
        clone.GetDDSPaths(clone.ImagePaths);

        clone.Subgroups.Clear();
        foreach (var subgroup in Subgroups)
        {
            clone.Subgroups.Add(subgroup.Clone(clone.Subgroups) as VM_Subgroup);
        }

        return clone;
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is VM_Subgroup && dropInfo.VisualTarget is ListBox)
        {
            var listBox = (ListBox)dropInfo.VisualTarget;
            var parentContextSubgroup = listBox.DataContext as VM_Subgroup;
            if (parentContextSubgroup != null)
            {
                var draggedSubgroup = (VM_Subgroup)dropInfo.Data;
                if (listBox.Name == "lbRequiredSubgroups")
                {
                    parentContextSubgroup.RequiredSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.RequiredSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.RequiredSubgroups, SubgroupListBox.Required);
                }
                else if (listBox.Name == "lbExcludedSubgroups")
                {
                    parentContextSubgroup.ExcludedSubgroups.Add(draggedSubgroup);
                    parentContextSubgroup.ExcludedSubgroupIDs.Add(draggedSubgroup.ID);
                    RefreshListBoxLabel(parentContextSubgroup.ExcludedSubgroups, SubgroupListBox.Excluded);
                }
            }
        }
    }

    public bool IsParentOf(VM_Subgroup candidateChild)
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

    public bool CheckForVersionUpdate(Version version)
    {
        if (version == Version.v090)
        {
            foreach (var path in PathsMenu.Paths)
            {
                string lastClass = path.IntellisensedPath.Split('.').Last();
                if (!path.DestinationExists && _updateHandler.V09PathReplacements.ContainsKey(lastClass))
                {
                    return true;
                }
            }

            foreach (var subgroup in Subgroups)
            {
                if (subgroup.CheckForVersionUpdate(version))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void PerformVersionUpdate(Version version)
    {
        if (version == Version.v090)
        {
            foreach (var path in PathsMenu.Paths)
            {
                string lastClass = path.IntellisensedPath.Split('.').Last();
                if (!path.DestinationExists && _updateHandler.V09PathReplacements.ContainsKey(lastClass))
                {
                    path.IntellisensedPath = MiscFunctions.ReplaceLastOccurrence(path.IntellisensedPath, lastClass, _updateHandler.V09PathReplacements[lastClass]);
                }
            }

            foreach (var subgroup in Subgroups)
            {
                subgroup.PerformVersionUpdate(version);
            }
        }
    }

    public List<string> GetRulesSummary()
    {
        List<string> rulesSummary = new();
        string tmpReport = "";
        if (_logger.GetRaceLogString("Allowed", AllowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Allowed", AllowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceLogString("Disallowed", DisallowedRaces, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetRaceGroupingLogString("Disallowed", DisallowedRaceGroupings, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Allowed", AllowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (_logger.GetAttributeLogString("Disallowed", DisallowedAttributes, out tmpReport)) { rulesSummary.Add(tmpReport); }
        if (!AllowUnique) { rulesSummary.Add("Unique NPCs: Disallowed"); }
        if (!AllowNonUnique) { rulesSummary.Add("Generic NPCs: Disallowed"); }
        if (ProbabilityWeighting != 1) { rulesSummary.Add("Probability Weighting: " + ProbabilityWeighting.ToString()); }
        if (WeightRange.Lower != 0 || WeightRange.Upper != 100) { rulesSummary.Add("Weight Range: " + WeightRange.Lower.ToString() + " to " + WeightRange.Upper.ToString()); }


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

    public void RefreshBodyGenDescriptors()
    {
        DescriptorMatchMode allowedMode = DescriptorMatchMode.All;
        DescriptorMatchMode disallowedMode = DescriptorMatchMode.Any;

        // keep existing settings if any
        if (AllowedBodyGenDescriptors != null)
        {
            allowedMode = AllowedBodyGenDescriptors.MatchMode;
        }
        if (DisallowedBodyGenDescriptors != null)
        {
            disallowedMode = DisallowedBodyGenDescriptors.MatchMode;
        }

        if (ParentAssetPack.TrackedBodyGenConfig != null)
        {
            AllowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, allowedMode);
            DisallowedBodyGenDescriptors = _descriptorSelectionFactory(ParentAssetPack.TrackedBodyGenConfig.DescriptorUI, SubscribedRaceGroupings, ParentAssetPack, true, disallowedMode);
        }
    }
}