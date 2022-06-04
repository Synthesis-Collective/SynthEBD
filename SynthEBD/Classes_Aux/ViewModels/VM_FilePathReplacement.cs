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
    public VM_FilePathReplacement(VM_FilePathReplacementMenu parentMenu)
    {
        
        ReferenceNPCFormKey = parentMenu.ReferenceNPCFK;
        LinkCache = parentMenu.ReferenceLinkCache;

        RecordIntellisense.InitializeSubscriptions(this);
        parentMenu.WhenAnyValue(x => x.ReferenceNPCFK).Subscribe(x => SyncReferenceWithParent()); // can be changed from record templates without the user modifying parentMenu.NPCFK, so need an explicit watch
        parentMenu.WhenAnyValue(x => x.ReferenceLinkCache).Subscribe(x => LinkCache = parentMenu.ReferenceLinkCache);

        DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentMenu.Paths.Remove(this));
        FindPath = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                System.Windows.Forms.OpenFileDialog dialog = LongPathHandler.CreateLongPathOpenFileDialog();
                if (Source != "")
                {
                    var initDir = Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, Path.GetDirectoryName(Source));
                    if (Directory.Exists(initDir))
                    {
                        dialog.InitialDirectory = initDir;
                    }
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // try to figure out the root directory
                    if (dialog.FileName.Contains(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath))
                    {
                        Source = dialog.FileName.Replace(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, "").TrimStart(Path.DirectorySeparatorChar);
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

        SetDestinationPath = new SynthEBD.RelayCommand(
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

        this.WhenAnyValue(x => x.Source).Subscribe(x => RefreshSourceColor());
        this.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => RefreshReferenceNPC());
        this.WhenAnyValue(x => x.ParentMenu.ReferenceNPCFK).Subscribe(x => RefreshReferenceNPC());
    }

    public VM_FilePathReplacement Clone(VM_FilePathReplacementMenu parentMenu)
    {
        VM_FilePathReplacement clone = new VM_FilePathReplacement(parentMenu);
        clone.Source = Source;
        clone.IntellisensedPath = this.IntellisensedPath;
        clone.ReferenceNPCFormKey = this.ReferenceNPCFormKey.DeepCopyByExpressionTree();
        return clone;
    }

    public string Source { get; set; } = "";
    public string IntellisensedPath { get; set; } = "";

    public string DestinationAlias { get; set; } = "";

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
            var references = ParentMenu.ParentSubgroup.ParentAssetPack.AdditionalRecordTemplateAssignments.Select(x => x.TemplateNPC).And(ParentMenu.ParentSubgroup.ParentAssetPack.DefaultTemplateFK);
            foreach (var referenceNPCformKey in references)
            {
                if (ParentMenu.ReferenceLinkCache.TryResolve<INpcGetter>(referenceNPCformKey, out var referenceNPCgetter) && RecordPathParser.GetObjectAtPath(referenceNPCgetter, IntellisensedPath, new Dictionary<string, dynamic>(), ParentMenu.ReferenceLinkCache, true, "", out _))
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
        var searchStr = Path.Combine(PatcherEnvironmentProvider.Instance.Environment.DataFolderPath, this.Source);
        if (LongPathHandler.PathExists(searchStr) || BSAHandler.ReferencedPathExists(this.Source, out _, out _))
        {
            this.SourceBorderColor = new SolidColorBrush(Colors.LightGreen);
        }
        else
        {
            this.SourceBorderColor = new SolidColorBrush(Colors.Red);
        }
    }

    public void RefreshDestColor()
    {
        if(LinkCache != null && ReferenceNPCFormKey != null && LinkCache.TryResolve<INpcGetter>(ReferenceNPCFormKey, out var refNPC) && RecordPathParser.GetObjectAtPath(refNPC, this.IntellisensedPath, new Dictionary<string, dynamic>(), ParentMenu.ReferenceLinkCache, true, Logger.GetNPCLogNameString(refNPC), out var objAtPath) && objAtPath is not null && objAtPath.GetType() == typeof(string))
        {
            this.DestBorderColor = new SolidColorBrush(Colors.LightGreen);
        }
        else
        {
            this.DestBorderColor = new SolidColorBrush(Colors.Red);
        }
    }

    private static bool TrimKnownPrefix(string s, out string trimmed)
    {
        trimmed = "";
        foreach (var trim in PatcherSettings.TexMesh.TrimPaths)
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
            this.ReferenceNPCFormKey = ParentMenu.ReferenceNPCFK;
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

            body.Add(female);

            main.Add(body);

            return menu;
        }
    }

    public static string GetPathFromTypeString(string typeString)
    {
        switch(typeString)
        {
            case "Head Diffuse": return "HeadTexture.Diffuse";
            case "Head Normal": return "HeadTexture.NormalOrGloss";
            case "Head Subsurface": return "HeadTexture.GlowOrDetailMap";
            case "Head Specular": return "HeadTexture.BacklightMaskOrSpecular";
            case "Head Detail": return "HeadTexture.Height";

            case "Torso Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse";
            case "Torso Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss";
            case "Torso Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap";
            case "Torso Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular";

            case "Hands Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse";
            case "Hands Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss";
            case "Hands Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap";
            case "Hands Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular";

            case "Feet Diffuse Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse";
            case "Feet Normal Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss";
            case "Feet Subsurface Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap";
            case "Feet Specular Male": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular";

            case "Torso Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse";
            case "Torso Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss";
            case "Torso Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap";
            case "Torso Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular";

            case "Hands Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse";
            case "Hands Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss";
            case "Hands Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap";
            case "Hands Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular";

            case "Feet Diffuse Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse";
            case "Feet Normal Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss";
            case "Feet Subsurface Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap";
            case "Feet Specular Female": return "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular";
        }
        return "";
    }
}