using System.Collections.ObjectModel;
using System.Windows.Media;
using System.IO;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using System.Reactive.Linq;

namespace SynthEBD;

/// <summary>
/// View model for a single source→destination file-path replacement row: a source asset path (with a
/// file picker and validity coloring) and a destination record path (with record intellisense, a
/// friendly "abstract" caption, and a menu of canned destinations), validated against a reference NPC.
/// </summary>
public class VM_FilePathReplacement : VM, IImplementsRecordIntellisense
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly BSAHandler _bsaHandler;
    private readonly RecordIntellisense _recordIntellisense;
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;
    private readonly Factory _selfFactory;

    /// <summary>Autofac factory delegate for constructing a replacement row under a menu.</summary>
    public delegate VM_FilePathReplacement Factory(VM_FilePathReplacementMenu parentMenu);

    /// <summary>Creates the row, wiring the delete / find-path / set-destination / toggle-view commands, record intellisense, and source/destination validity refresh subscriptions.</summary>
    /// <param name="parentMenu">The owning path-replacement menu.</param>
    /// <param name="environmentProvider">Supplies the data-folder path and link cache.</param>
    /// <param name="patcherState">Patcher state (trim-path settings).</param>
    /// <param name="bsaHandler">BSA handler used to check source existence inside archives.</param>
    /// <param name="recordIntellisense">Record-path intellisense provider.</param>
    /// <param name="recordPathParser">Parser used to validate destination paths.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="selfFactory">Factory used by <see cref="Clone"/>.</param>
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
                        MessageWindow.DisplayNotificationOK("Parsing Error", "Cannot figure out where the Data folder is within the supplied path. You will need to edit the path so that it starts one folder beneath the Data folder.");
                        Source = dialog.FileName;
                    }

                    RefreshSourceColor();
                    if (SourceBorderColor == BorderColorValid)
                    {
                        ParentMenu.ParentSubgroup.AssociatedPlaceHolder.GetDDSPaths();
                        ParentMenu.ParentSubgroup.AssociatedPlaceHolder.ImagePreviewRefreshTrigger++;
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

        ToggleDestinationView = new RelayCommand(
            canExecute: _ => true,
            execute: _ => {
                ShowDestinationAbstractView = !ShowDestinationAbstractView;
                ShowDestinationDetailView = !ShowDestinationDetailView;
            }
        );

        ParentMenu = parentMenu;

        this.WhenAnyValue(x => x.Source).Subscribe(x =>
        {
            RefreshSourceColor();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x =>
        {
            RefreshReferenceNPC();
            RefreshAbstractView();
        }).DisposeWith(this);

        this.WhenAnyValue(x => x.ParentMenu.ReferenceNPCFK).Subscribe(x => RefreshReferenceNPC()).DisposeWith(this);
    }

    /// <summary>Creates a copy of this row under a (possibly different) parent menu.</summary>
    /// <param name="parentMenu">The parent menu for the clone.</param>
    /// <returns>The cloned row.</returns>
    public VM_FilePathReplacement Clone(VM_FilePathReplacementMenu parentMenu)
    {
        VM_FilePathReplacement clone = _selfFactory(parentMenu);
        clone.Source = Source;
        clone.IntellisensedPath = IntellisensedPath;
        clone.ReferenceNPCFormKey = ReferenceNPCFormKey.DeepCopyByExpressionTree();
        return clone;
    }

    public string Source { get; set; } = "";
    public string IntellisensedPath { get; set; } = "";

    public bool SourceExists { get; set; } = false;
    public bool DestinationExists { get; set; } = false;

    public static SolidColorBrush BorderColorValid = CommonColors.LightGreen;
    public static SolidColorBrush BorderColorInvalid = CommonColors.Red;

    public SolidColorBrush SourceBorderColor { get; set; } = BorderColorInvalid;
    public SolidColorBrush DestBorderColor { get; set; } = BorderColorInvalid;

    public RelayCommand DeleteCommand { get; }
    public RelayCommand FindPath { get; }
    public RelayCommand SetDestinationPath { get; }
    public VM_FilePathReplacementMenu ParentMenu { get; set; }
    public RecordIntellisense.PathSuggestion ChosenPathSuggestion { get; set; } = new();
    public ObservableCollection<RecordIntellisense.PathSuggestion> PathSuggestions { get; set; } = new();
    public FormKey ReferenceNPCFormKey { get; set; }
    public ILinkCache LinkCache { get; set; }
    public RelayCommand ToggleDestinationView { get; }
    public bool ShowDestinationDetailView { get; set; } = false;
    public bool ShowDestinationAbstractView { get; set; } = true;
    private const string destinationCustomView = "Custom Path";
    public string DestinationAbstractView { get; set; } = destinationCustomView;
    private Dictionary<string, string> DestinationDetailAbstractDictionary { get; set; } = new()
    {
        // Head mappings
        { FilePathDestinationMap.Dest_HeadDiffuse, "Head Diffuse" },
        { FilePathDestinationMap.Dest_HeadNormal, "Head Normal" },
        { FilePathDestinationMap.Dest_HeadSubsurface, "Head Subsurface" },
        { FilePathDestinationMap.Dest_HeadSpecular, "Head Specular" },
        { FilePathDestinationMap.Dest_HeadDetail, "Head Detail" },

        // Torso Male mappings
        { FilePathDestinationMap.Dest_TorsoMaleDiffuse, "Torso Diffuse (Male)" },
        { FilePathDestinationMap.Dest_TorsoMaleNormal, "Torso Normal (Male)" },
        { FilePathDestinationMap.Dest_TorsoMaleSubsurface, "Torso Subsurface (Male)" },
        { FilePathDestinationMap.Dest_TorsoMaleSpecular, "Torso Specular (Male)" },

        // Hands Male mappings
        { FilePathDestinationMap.Dest_HandsMaleDiffuse, "Hands Diffuse (Male)" },
        { FilePathDestinationMap.Dest_HandsMaleNormal, "Hands Normal (Male)" },
        { FilePathDestinationMap.Dest_HandsMaleSubsurface, "Hands Subsurface (Male)" },
        { FilePathDestinationMap.Dest_HandsMaleSpecular, "Hands Specular (Male)" },

        // Feet Male mappings
        { FilePathDestinationMap.Dest_FeetMaleDiffuse, "Feet Diffuse (Male)" },
        { FilePathDestinationMap.Dest_FeetMaleNormal, "Feet Normal (Male)" },
        { FilePathDestinationMap.Dest_FeetMaleSubsurface, "Feet Subsurface (Male)" },
        { FilePathDestinationMap.Dest_FeetMaleSpecular, "Feet Specular (Male)" },

        // Tail Male mappings
        { FilePathDestinationMap.Dest_TailMaleDiffuse, "Tail Diffuse (Male)" },
        { FilePathDestinationMap.Dest_TailMaleNormal, "Tail Normal (Male)" },
        { FilePathDestinationMap.Dest_TailMaleSubsurface, "Tail Subsurface (Male)" },
        { FilePathDestinationMap.Dest_TailMaleSpecular, "Tail Specular (Male)" },

        // Torso Female mappings
        { FilePathDestinationMap.Dest_TorsoFemaleDiffuse, "Torso Diffuse (Female)" },
        { FilePathDestinationMap.Dest_TorsoFemaleNormal, "Torso Normal (Female)" },
        { FilePathDestinationMap.Dest_TorsoFemaleSubsurface, "Torso Subsurface (Female)" },
        { FilePathDestinationMap.Dest_TorsoFemaleSpecular, "Torso Specular (Female)" },

        // Hands Female mappings
        { FilePathDestinationMap.Dest_HandsFemaleDiffuse, "Hands Diffuse (Female)" },
        { FilePathDestinationMap.Dest_HandsFemaleNormal, "Hands Normal (Female)" },
        { FilePathDestinationMap.Dest_HandsFemaleSubsurface, "Hands Subsurface (Female)" },
        { FilePathDestinationMap.Dest_HandsFemaleSpecular, "Hands Specular (Female)" },

        // Feet Female mappings
        { FilePathDestinationMap.Dest_FeetFemaleDiffuse, "Feet Diffuse (Female)" },
        { FilePathDestinationMap.Dest_FeetFemaleNormal, "Feet Normal (Female)" },
        { FilePathDestinationMap.Dest_FeetFemaleSubsurface, "Feet Subsurface (Female)" },
        { FilePathDestinationMap.Dest_FeetFemaleSpecular, "Feet Specular (Female)" },

        // Tail Female mappings
        { FilePathDestinationMap.Dest_TailFemaleDiffuse, "Tail Diffuse (Female)" },
        { FilePathDestinationMap.Dest_TailFemaleNormal, "Tail Normal (Female)" },
        { FilePathDestinationMap.Dest_TailFemaleSubsurface, "Tail Subsurface (Female)" },
        { FilePathDestinationMap.Dest_TailFemaleSpecular, "Tail Specular (Female)" },
    };

    /// <summary>Populates this row's source and destination from a <see cref="FilePathReplacement"/> model.</summary>
    /// <param name="model">The model to load.</param>
    public void CopyInViewModelFromModel(FilePathReplacement model)
    {
        Source = model.Source;
        IntellisensedPath = model.Destination;
    }

    /// <summary>Updates the friendly "abstract" destination caption from the current intellisensed path (a known friendly name, or "Custom Path: …").</summary>
    private void RefreshAbstractView()
    {
        if (DestinationDetailAbstractDictionary.ContainsKey(IntellisensedPath))
        {
            DestinationAbstractView = DestinationDetailAbstractDictionary[IntellisensedPath];
        }
        else
        {
            DestinationAbstractView = destinationCustomView + ": " + IntellisensedPath;
        }
    }

    /// <summary>Resolves the reference NPC against which the destination is validated — explicit, or by probing the asset pack's record templates for one where the path resolves — then refreshes the destination color (off the UI thread).</summary>
    public void RefreshReferenceNPC()
    {
        Task.Run(() =>
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
        });
    }

    /// <summary>Sets the source validity color based on whether the source asset exists on disk or in an associated BSA.</summary>
    public void RefreshSourceColor()
    {
        var searchStr = Path.Combine(_environmentProvider.DataFolderPath, Source);
        if (!Source.IsNullOrWhitespace() && (LongPathHandler.PathExists(searchStr) || _bsaHandler.ReferencedPathExists(Source, out _, out _) || _bsaHandler.ReferencedPathExists(Source, ParentMenu.ParentSubgroup.ParentAssetPack.MiscMenu.AssociatedBsaModKeys, out _, out _)))
        {
            SourceExists = true;
            SourceBorderColor = BorderColorValid;
        }
        else
        {
            SourceExists = false;
            SourceBorderColor = BorderColorInvalid;
        }
    }

    /// <summary>Sets the destination validity color based on whether the destination record path resolves to a string on the reference NPC.</summary>
    public void RefreshDestColor()
    {
        if (DestinationPathExists(IntellisensedPath, LinkCache, ReferenceNPCFormKey, _recordPathParser, _logger))
        {
            DestinationExists = true;
            DestBorderColor = BorderColorValid;
        }
        else
        {
            DestinationExists = false;
            DestBorderColor = BorderColorInvalid;
        }
    }

    /// <summary>Determines whether a destination record path resolves to a string value on the given reference NPC.</summary>
    /// <param name="destinationPath">The destination record path.</param>
    /// <param name="linkCache">Link cache for resolution.</param>
    /// <param name="referenceNPCFormKey">The reference NPC to resolve against.</param>
    /// <param name="recordPathParser">Parser used to walk the path.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns><c>true</c> if the path resolves to a string.</returns>
    public static bool DestinationPathExists(string destinationPath, ILinkCache linkCache, FormKey referenceNPCFormKey, RecordPathParser recordPathParser, Logger logger)
    {
        if (!destinationPath.IsNullOrWhitespace() && 
            linkCache != null &&
            referenceNPCFormKey != null && 
            linkCache.TryResolve<INpcGetter>(referenceNPCFormKey, out var refNPC) && 
            recordPathParser.GetObjectAtPath(refNPC, refNPC, destinationPath, new Dictionary<string, dynamic>(), linkCache, true, logger.GetNPCLogNameString(refNPC), out var objAtPath) && 
            objAtPath is not null && 
            objAtPath.GetType() == typeof(string))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>Determines whether a destination record path resolves on any of the given reference NPCs.</summary>
    /// <param name="destinationPath">The destination record path.</param>
    /// <param name="linkCache">Link cache for resolution.</param>
    /// <param name="referenceNPCFormKeys">The reference NPCs to try.</param>
    /// <param name="recordPathParser">Parser used to walk the path.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns><c>true</c> if the path resolves on any reference NPC.</returns>
    public static bool DestinationPathExists(string destinationPath, ILinkCache linkCache, IEnumerable<FormKey> referenceNPCFormKeys, RecordPathParser recordPathParser, Logger logger)
    {
        foreach (var referenceFormKey in referenceNPCFormKeys)
        {
            if (DestinationPathExists(destinationPath, linkCache, referenceFormKey, recordPathParser, logger))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Trims a configured known path prefix off a picked file path, if one matches by fragment and extension.</summary>
    /// <param name="s">The full picked file path.</param>
    /// <param name="trimmed">Receives the trimmed (Data-relative) path on success.</param>
    /// <returns><c>true</c> if a known prefix matched and was trimmed.</returns>
    private bool TrimKnownPrefix(string s, out string trimmed)
    {
        trimmed = "";
        foreach (var trim in _patcherState.TexMeshSettings.TrimPaths)
        {
            if (s.Contains(trim.PathToTrim, StringComparison.OrdinalIgnoreCase) && s.EndsWith(trim.Extension, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = s.Remove(0, s.IndexOf(trim.PathToTrim, StringComparison.OrdinalIgnoreCase)).TrimStart(Path.DirectorySeparatorChar);
                return true;
            }
        }
        return false;
    }

    /// <summary>Syncs the reference NPC from the parent menu (it can change via record templates without the user editing the menu directly).</summary>
    private void SyncReferenceWithParent()
    {
        if (ParentMenu != null)
        {
            ReferenceNPCFormKey = ParentMenu.ReferenceNPCFK;
        }
    }

    /// <summary>Builds the hierarchical "Set Destination" context menu of canned head/body destination paths (per body part, slot, and sex), each wired to <c>SetDestinationPath</c>.</summary>
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

    /// <summary>Maps a friendly destination name (e.g. "Torso Diffuse Male") to its record-path DSL string.</summary>
    /// <param name="typeString">The friendly destination name.</param>
    /// <returns>The record path, or "" if unrecognized.</returns>
    public static string GetPathFromTypeString(string typeString)
    {
        switch(typeString)
        {
            case "Head Diffuse": return FilePathDestinationMap.Dest_HeadDiffuse;
            case "Head Normal": return FilePathDestinationMap.Dest_HeadNormal;
            case "Head Subsurface": return FilePathDestinationMap.Dest_HeadSubsurface;
            case "Head Specular": return FilePathDestinationMap.Dest_HeadSpecular;
            case "Head Detail": return FilePathDestinationMap.Dest_HeadDetail;

            case "Torso Diffuse Male": return FilePathDestinationMap.Dest_TorsoMaleDiffuse;
            case "Torso Normal Male": return FilePathDestinationMap.Dest_TorsoMaleNormal;
            case "Torso Subsurface Male": return FilePathDestinationMap.Dest_TorsoMaleSubsurface;
            case "Torso Specular Male": return FilePathDestinationMap.Dest_TorsoMaleSpecular;

            case "Hands Diffuse Male": return FilePathDestinationMap.Dest_HandsMaleDiffuse;
            case "Hands Normal Male": return FilePathDestinationMap.Dest_HandsMaleNormal;
            case "Hands Subsurface Male": return FilePathDestinationMap.Dest_HandsMaleSubsurface;
            case "Hands Specular Male": return FilePathDestinationMap.Dest_HandsMaleSpecular;

            case "Feet Diffuse Male": return FilePathDestinationMap.Dest_FeetMaleDiffuse;
            case "Feet Normal Male": return FilePathDestinationMap.Dest_FeetMaleNormal;
            case "Feet Subsurface Male": return FilePathDestinationMap.Dest_FeetMaleSubsurface;
            case "Feet Specular Male": return FilePathDestinationMap.Dest_FeetMaleSpecular;

            case "Tail Diffuse Male": return FilePathDestinationMap.Dest_TailMaleDiffuse;
            case "Tail Normal Male": return FilePathDestinationMap.Dest_TailMaleNormal;
            case "Tail Subsurface Male": return FilePathDestinationMap.Dest_TailMaleSubsurface;
            case "Tail Specular Male": return FilePathDestinationMap.Dest_TailMaleSpecular;

            case "Torso Diffuse Female": return FilePathDestinationMap.Dest_TorsoFemaleDiffuse;
            case "Torso Normal Female": return FilePathDestinationMap.Dest_TorsoFemaleNormal;
            case "Torso Subsurface Female": return FilePathDestinationMap.Dest_TorsoFemaleSubsurface;
            case "Torso Specular Female": return FilePathDestinationMap.Dest_TorsoFemaleSpecular;

            case "Hands Diffuse Female": return FilePathDestinationMap.Dest_HandsFemaleDiffuse;
            case "Hands Normal Female": return FilePathDestinationMap.Dest_HandsFemaleNormal;
            case "Hands Subsurface Female": return FilePathDestinationMap.Dest_HandsFemaleSubsurface;
            case "Hands Specular Female": return FilePathDestinationMap.Dest_HandsFemaleSpecular;

            case "Feet Diffuse Female": return FilePathDestinationMap.Dest_FeetFemaleDiffuse;
            case "Feet Normal Female": return FilePathDestinationMap.Dest_FeetFemaleNormal;
            case "Feet Subsurface Female": return FilePathDestinationMap.Dest_FeetFemaleSubsurface;
            case "Feet Specular Female": return FilePathDestinationMap.Dest_FeetFemaleSpecular;

            case "Tail Diffuse Female": return FilePathDestinationMap.Dest_TailFemaleDiffuse;
            case "Tail Normal Female": return FilePathDestinationMap.Dest_TailFemaleNormal;
            case "Tail Subsurface Female": return FilePathDestinationMap.Dest_TailFemaleSubsurface;
            case "Tail Specular Female": return FilePathDestinationMap.Dest_TailFemaleSpecular;
        }
        return "";
    }
}