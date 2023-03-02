using System.Collections.ObjectModel;
using System.Windows.Media;
using System.IO;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;

namespace SynthEBD;

public class VM_FilePathReplacement : VM, IImplementsRecordIntellisense
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly BSAHandler _bsaHandler;
    private readonly RecordIntellisense _recordIntellisense;
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;
    private readonly Factory _selfFactory;

    public delegate VM_FilePathReplacement Factory(VM_FilePathReplacementMenu parentMenu);
    
    public VM_FilePathReplacement(VM_FilePathReplacementMenu parentMenu, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, BSAHandler bsaHandler, RecordIntellisense recordIntellisense, RecordPathParser recordPathParser, Logger logger, Factory selfFactory)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _bsaHandler = bsaHandler;
        _recordIntellisense = recordIntellisense;
        _recordPathParser = recordPathParser;
        _logger = logger;
        _selfFactory = selfFactory;
        ReferenceNPCFormKey = parentMenu.ReferenceNPCFK;
        LinkCache = parentMenu.ReferenceLinkCache;

        _recordIntellisense.InitializeSubscriptions(this);
        parentMenu.WhenAnyValue(x => x.ReferenceNPCFK).Subscribe(x => SyncReferenceWithParent()).DisposeWith(this); // can be changed from record templates without the user modifying parentMenu.NPCFK, so need an explicit watch
        parentMenu.WhenAnyValue(x => x.ReferenceLinkCache).Subscribe(x => LinkCache = parentMenu.ReferenceLinkCache).DisposeWith(this);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentMenu.Paths.Remove(this));
        FindPath = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                System.Windows.Forms.OpenFileDialog dialog = LongPathHandler.CreateLongPathOpenFileDialog();
                if (Source != "")
                {
                    var initDir = Path.Combine(_environmentProvider.DataFolderPath, Path.GetDirectoryName(Source));
                    if (Directory.Exists(initDir))
                    {
                        dialog.InitialDirectory = initDir;
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // try to figure out the root directory
                    if (dialog.FileName.Contains(_environmentProvider.DataFolderPath))
                    {
                        Source = dialog.FileName.Replace(_environmentProvider.DataFolderPath, "").TrimStart(Path.DirectorySeparatorChar);
                    }
                    else if (TrimKnownPrefix(dialog.FileName, out var sourceTrimmed))
                    {
                        Source = sourceTrimmed;
                    }
                    else if (dialog.FileName.Contains("Data", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var index = dialog.FileName.IndexOf("Data", 0, StringComparison.InvariantCultureIgnoreCase);
                        Source = dialog.FileName.Remove(0, index + 4).TrimStart(Path.DirectorySeparatorChar);
                    }
                    else
                    {
                        CustomMessageBox.DisplayNotificationOK("Parsing Error", "Cannot figure out where the Data folder is within the supplied path. You will need to edit the path so that it starts one folder beneath the Data folder.");
                        Source = dialog.FileName;
                    }

                    if (string.IsNullOrWhiteSpace(IntellisensedPath) && FilePathDestinationMap.FileNameToDestMap.ContainsKey(Path.GetFileName(Source)))
                    {
                        IntellisensedPath = FilePathDestinationMap.FileNameToDestMap[Path.GetFileName(Source)];
                    }
                }
            }
        );

        SetDestinationPath = new RelayCommand(
            canExecute: _ => true,
            execute: x => { 
                var selectedItem = (VM_MenuItem)x;
                if (selectedItem != null && !string.IsNullOrWhiteSpace(selectedItem.Alias))
                {
                    IntellisensedPath = GetPathFromTypeString(selectedItem.Alias);
                }           
            }
        );

        ParentMenu = parentMenu;

        this.WhenAnyValue(x => x.Source).Subscribe(x => RefreshSourceColor()).DisposeWith(this);
        this.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => RefreshReferenceNPC()).DisposeWith(this);
        this.WhenAnyValue(x => x.ParentMenu.ReferenceNPCFK).Subscribe(x => RefreshReferenceNPC()).DisposeWith(this);
    }

    public VM_FilePathReplacement Clone(VM_FilePathReplacementMenu parentMenu)
    {
        VM_FilePathReplacement clone = _selfFactory(parentMenu);
        clone.Source = Source;
        clone.IntellisensedPath = this.IntellisensedPath;
        clone.ReferenceNPCFormKey = this.ReferenceNPCFormKey.DeepCopyByExpressionTree();
        return clone;
    }

    public string Source { get; set; } = "";
    public string IntellisensedPath { get; set; } = "";

    public bool SourceExists { get; set; } = false;
    public bool DestinationExists { get; set; } = false;

    public SolidColorBrush SourceBorderColor { get; set; } = new(Colors.Red);
    public SolidColorBrush DestBorderColor { get; set; } = new(Colors.Red);

    public RelayCommand DeleteCommand { get; }
    public RelayCommand FindPath { get; }
    public RelayCommand SetDestinationPath { get; }
    public VM_FilePathReplacementMenu ParentMenu { get; set; }
    public RecordIntellisense.PathSuggestion ChosenPathSuggestion { get; set; } = new();
    public ObservableCollection<RecordIntellisense.PathSuggestion> PathSuggestions { get; set; } = new();
    public FormKey ReferenceNPCFormKey { get; set; }
    public ILinkCache LinkCache { get; set; }

    public void CopyInViewModelFromModel(FilePathReplacement model)
    {
        Source = model.Source;
        IntellisensedPath = model.Destination;
    }

    public void RefreshReferenceNPC()
    {
        if (ParentMenu.SetExplicitReferenceNPC)
        {
            ReferenceNPCFormKey = ParentMenu.ReferenceNPCFK;
        }
        else
        {
            var references = ParentMenu.ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments.Select(x => x.TemplateNPC).And(ParentMenu.ParentSubgroup.ParentAssetPack.DefaultTemplateFK).ToArray();
            foreach (var referenceNPCformKey in references)
            {
                if (ParentMenu.ReferenceLinkCache.TryResolve<INpcGetter>(referenceNPCformKey, out var referenceNPCgetter) && _recordPathParser.GetObjectAtPath(referenceNPCgetter, referenceNPCgetter, IntellisensedPath, new Dictionary<string, dynamic>(), ParentMenu.ReferenceLinkCache, true, "", out _))
                {
                    ReferenceNPCFormKey = referenceNPCformKey;
                    break;
                }
            }
        }
        RefreshDestColor();
    }

    public void RefreshSourceColor()
    {
        var searchStr = Path.Combine(_environmentProvider.DataFolderPath, Source);
        if (!Source.IsNullOrWhitespace() && (LongPathHandler.PathExists(searchStr) || _bsaHandler.ReferencedPathExists(Source, out _, out _) || _bsaHandler.ReferencedPathExists(Source, ParentMenu.ParentSubgroup.ParentAssetPack.MiscMenu.AssociatedBsaModKeys, out _, out _)))
        {
            SourceExists = true;
            SourceBorderColor = new SolidColorBrush(Colors.LightGreen);
        }
        else
        {
            SourceExists = false;
            SourceBorderColor = new SolidColorBrush(Colors.Red);
        }
    }

    public void RefreshDestColor()
    {
        if (!IntellisensedPath.IsNullOrWhitespace() && LinkCache != null && ReferenceNPCFormKey != null && LinkCache.TryResolve<INpcGetter>(ReferenceNPCFormKey, out var refNPC) && _recordPathParser.GetObjectAtPath(refNPC, refNPC, IntellisensedPath, new Dictionary<string, dynamic>(), ParentMenu.ReferenceLinkCache, true, _logger.GetNPCLogNameString(refNPC), out var objAtPath) && objAtPath is not null && objAtPath.GetType() == typeof(string))
        {
            DestinationExists = true;
            DestBorderColor = new SolidColorBrush(Colors.LightGreen);
        }
        else
        {
            DestinationExists = false;
            DestBorderColor = new SolidColorBrush(Colors.Red);
        }
    }

    private bool TrimKnownPrefix(string s, out string trimmed)
    {
        trimmed = "";
        foreach (var trim in _patcherState.TexMeshSettings.TrimPaths)
        {
            if (s.Contains(trim.PathToTrim) && s.EndsWith(trim.Extension))
            {
                trimmed = s.Remove(0, s.IndexOf(trim.PathToTrim, StringComparison.OrdinalIgnoreCase)).TrimStart(Path.DirectorySeparatorChar);
                return true;
            }
        }
        return false;
    }

    private void SyncReferenceWithParent()
    {
        if (ParentMenu != null)
        {
            ReferenceNPCFormKey = ParentMenu.ReferenceNPCFK;
        }
    }

    public ObservableCollection<VM_MenuItem> DestinationOptions
    {
        get
        {
            var menu = new ObservableCollection<VM_MenuItem>();
            var main = new VM_MenuItem { Header = "Set Destination" };
            menu.Add(main);

            var head = new VM_MenuItem { Header = "Head" };
            head.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Head Diffuse", Command = SetDestinationPath });
            head.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Head Normal", Command = SetDestinationPath });
            head.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Head Subsurface", Command = SetDestinationPath });
            head.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Head Specular", Command = SetDestinationPath });
            head.Children.Add(new VM_MenuItem() { Header = "Detail", Alias = "Head Detail", Command = SetDestinationPath });
            main.Add(head);

            var body = new VM_MenuItem { Header = "Body" };

            var male = new VM_MenuItem { Header = "Male" };

            var torsoMale = new VM_MenuItem { Header = "Torso" };
            torsoMale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Torso Diffuse Male", Command = SetDestinationPath });
            torsoMale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Torso Normal Male", Command = SetDestinationPath });
            torsoMale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Torso Subsurface Male", Command = SetDestinationPath });
            torsoMale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Torso Specular Male", Command = SetDestinationPath });
            male.Add(torsoMale);

            var handsMale = new VM_MenuItem { Header = "Hands" };
            handsMale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Hands Diffuse Male", Command = SetDestinationPath });
            handsMale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Hands Normal Male", Command = SetDestinationPath });
            handsMale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Hands Subsurface Male", Command = SetDestinationPath });
            handsMale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Hands Specular Male", Command = SetDestinationPath });
            male.Add(handsMale);

            var feetMale = new VM_MenuItem { Header = "Feet" };
            feetMale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Feet Diffuse Male", Command = SetDestinationPath });
            feetMale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Feet Normal Male", Command = SetDestinationPath });
            feetMale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Feet Subsurface Male", Command = SetDestinationPath });
            feetMale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Feet Specular Male", Command = SetDestinationPath });
            male.Add(feetMale);

            var tailMale = new VM_MenuItem { Header = "Tail" };
            tailMale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Tail Diffuse Male", Command = SetDestinationPath });
            tailMale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Tail Normal Male", Command = SetDestinationPath });
            tailMale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Tail Subsurface Male", Command = SetDestinationPath });
            tailMale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Tail Specular Male", Command = SetDestinationPath });
            male.Add(tailMale);

            body.Add(male);

            var female = new VM_MenuItem { Header = "Female" };

            var torsoFemale = new VM_MenuItem { Header = "Torso" };
            torsoFemale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Torso Diffuse Female", Command = SetDestinationPath });
            torsoFemale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Torso Normal Female", Command = SetDestinationPath });
            torsoFemale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Torso Subsurface Female", Command = SetDestinationPath });
            torsoFemale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Torso Specular Female", Command = SetDestinationPath });
            female.Add(torsoFemale);

            var handsFemale = new VM_MenuItem { Header = "Hands" };
            handsFemale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Hands Diffuse Female", Command = SetDestinationPath });
            handsFemale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Hands Normal Female", Command = SetDestinationPath });
            handsFemale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Hands Subsurface Female", Command = SetDestinationPath });
            handsFemale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Hands Specular Female", Command = SetDestinationPath });
            female.Add(handsFemale);

            var feetFemale = new VM_MenuItem { Header = "Feet" };
            feetFemale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Feet Diffuse Female", Command = SetDestinationPath });
            feetFemale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Feet Normal Female", Command = SetDestinationPath });
            feetFemale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Feet Subsurface Female", Command = SetDestinationPath });
            feetFemale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Feet Specular Female", Command = SetDestinationPath });
            female.Add(feetFemale);

            var tailFemale = new VM_MenuItem { Header = "Tail" };
            tailFemale.Children.Add(new VM_MenuItem() { Header = "Diffuse", Alias = "Tail Diffuse Female", Command = SetDestinationPath });
            tailFemale.Children.Add(new VM_MenuItem() { Header = "Normal", Alias = "Tail Normal Female", Command = SetDestinationPath });
            tailFemale.Children.Add(new VM_MenuItem() { Header = "Subsurface", Alias = "Tail Subsurface Female", Command = SetDestinationPath });
            tailFemale.Children.Add(new VM_MenuItem() { Header = "Specular", Alias = "Tail Specular Female", Command = SetDestinationPath });
            female.Add(tailFemale);

            body.Add(female);

            main.Add(body);

            return menu;
        }
    }

    public static string GetPathFromTypeString(string typeString)
    {
        switch(typeString)
        {
            case "Head Diffuse": return "HeadTexture.Diffuse.RawPath";
            case "Head Normal": return "HeadTexture.NormalOrGloss.RawPath";
            case "Head Subsurface": return "HeadTexture.GlowOrDetailMap.RawPath";
            case "Head Specular": return "HeadTexture.BacklightMaskOrSpecular.RawPath";
            case "Head Detail": return "HeadTexture.Height.RawPath";

            case "Torso Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
            case "Torso Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
            case "Torso Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
            case "Torso Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

            case "Hands Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
            case "Hands Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
            case "Hands Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
            case "Hands Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

            case "Feet Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
            case "Feet Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
            case "Feet Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
            case "Feet Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

            case "Tail Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
            case "Tail Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
            case "Tail Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
            case "Tail Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

            case "Torso Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
            case "Torso Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
            case "Torso Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
            case "Torso Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

            case "Hands Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
            case "Hands Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
            case "Hands Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
            case "Hands Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

            case "Feet Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
            case "Feet Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
            case "Feet Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
            case "Feet Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

            case "Tail Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
            case "Tail Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
            case "Tail Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
            case "Tail Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";
        }
        return "";
    }
}