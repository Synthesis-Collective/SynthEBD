using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using System.ComponentModel;
using System.Windows;
using ReactiveUI;
using System.Reactive.Linq;

namespace SynthEBD;

public class MainWindow_ViewModel : VM
{
    public VM_Settings_General GeneralSettingsVM { get; }
    public VM_SettingsTexMesh TexMeshSettingsVM { get; }
    public VM_SettingsBodyGen BodyGenSettingsVM { get; }
    public VM_SettingsOBody OBodySettingsVM { get; }
    public VM_SettingsHeight HeightSettingsVM { get; } = new();
    public VM_SpecificNPCAssignmentsUI SpecificAssignmentsUIVM { get; }
    public VM_ConsistencyUI ConsistencyUIVM { get; } = new();
    public VM_BlockListUI BlockListVM { get; } = new();
    public VM_SettingsModManager ModManagerSettingsVM { get; } = new();
    public VM_NavPanel NavPanelVM { get; }

    public VM_RunButton RunButtonVM { get; }
    public object DisplayedViewModel { get; set; }
    public object NavViewModel { get; set; }

    public VM_StatusBar StatusBarVM { get; set; } = new();

    public VM_LogDisplay LogDisplayVM { get; set; } = new();
    public List<AssetPack> AssetPacks { get; set; }
    public List<HeightConfig> HeightConfigs { get; set; }
    public BodyGenConfigs BodyGenConfigs { get; set; }
    public Dictionary<string, NPCAssignment> Consistency { get; set; }
    public HashSet<NPCAssignment> SpecificNPCAssignments { get; set; }
    public BlockList BlockList { get; set; }
    public HashSet<string> LinkedNPCNameExclusions { get; set; }
    public HashSet<LinkedNPCGroup> LinkedNPCGroups { get; set; }

    public List<SkyrimMod> RecordTemplatePlugins { get; set; }
    public ILinkCache<ISkyrimMod, ISkyrimModGetter> RecordTemplateLinkCache { get; set; }

    public MainWindow_ViewModel()
    {
        // initialize logger
        Logger.Instance.MainVM = this;

        // initialize paths
        SettingsIO_Misc.GetSettingsSource();

        // Load settings
        GeneralSettingsVM = new VM_Settings_General(this);
        BodyGenSettingsVM = new VM_SettingsBodyGen(GeneralSettingsVM);
        OBodySettingsVM = new VM_SettingsOBody(GeneralSettingsVM.RaceGroupings, GeneralSettingsVM);
        TexMeshSettingsVM = new VM_SettingsTexMesh(this);
        SpecificAssignmentsUIVM = new VM_SpecificNPCAssignmentsUI(TexMeshSettingsVM, BodyGenSettingsVM, OBodySettingsVM, GeneralSettingsVM, this);
        NavPanelVM = new VM_NavPanel(this);
        RunButtonVM = new VM_RunButton(this);

        LoadInitialSettingsViewModels();
        LoadPluginViewModels();
        LoadFinalSettingsViewModels();

        // Start on the settings VM
        DisplayedViewModel = GeneralSettingsVM;
        NavViewModel = NavPanelVM;
        Logger.Instance.RunButton = RunButtonVM;

        Application.Current.MainWindow.Closing += new CancelEventHandler(MainWindow_Closing);

        ValidateEval();
    }

    public void SaveAndRefreshPlugins()
    {
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        SavePluginViewModels();
        LoadPluginViewModels();
        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
    }

    public void LoadInitialSettingsViewModels() // view models that should be loaded before plugin VMs
    {
        bool loadSuccess;

        // Load general settings
        SettingsIO_General.LoadGeneralSettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_Settings_General.GetViewModelFromModel(GeneralSettingsVM);

        // Initialize patchable races from general settings (required by some UI elements)
        Patcher.ResolvePatchableRaces();

        // Load texture and mesh settings
        PatcherSettings.TexMesh = SettingsIO_AssetPack.LoadTexMeshSettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_SettingsTexMesh.GetViewModelFromModel(TexMeshSettingsVM, PatcherSettings.TexMesh);

        PatcherSettings.BodyGen = SettingsIO_BodyGen.LoadBodyGenSettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }

        // load OBody settings before asset packs - asset packs depend on BodyGen but not vice versa
        PatcherSettings.OBody = SettingsIO_OBody.LoadOBodySettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        PatcherSettings.OBody.ImportBodySlides(PatcherSettings.OBody.TemplateDescriptors);

        // load heights
        PatcherSettings.Height = SettingsIO_Height.LoadHeightSettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }

        // load BlockList
        BlockList = SettingsIO_BlockList.LoadBlockList(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_BlockListUI.GetViewModelFromModel(BlockList, BlockListVM);

        // load Mod Manager Integration
        PatcherSettings.ModManagerIntegration = SettingsIO_ModManager.LoadModManagerSettings(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_SettingsModManager.GetViewModelFromModel(PatcherSettings.ModManagerIntegration, ModManagerSettingsVM);

        // load Misc settings
        LinkedNPCNameExclusions = SettingsIO_Misc.LoadNPCNameExclusions(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        GeneralSettingsVM.LinkedNameExclusions = VM_CollectionMemberString.InitializeCollectionFromHashSet(LinkedNPCNameExclusions);

        LinkedNPCGroups = SettingsIO_Misc.LoadLinkedNPCGroups(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        GeneralSettingsVM.LinkedNPCGroups = VM_LinkedNPCGroup.GetViewModelsFromModels(LinkedNPCGroups);
    }

    public void LoadPluginViewModels()
    {
        bool loadSuccess;

        // load bodygen configs before asset packs - asset packs depend on BodyGen but not vice versa
        BodyGenConfigs = SettingsIO_BodyGen.LoadBodyGenConfigs(PatcherSettings.General.RaceGroupings, out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_SettingsBodyGen.GetViewModelFromModel(BodyGenConfigs, PatcherSettings.BodyGen, BodyGenSettingsVM, GeneralSettingsVM);

        VM_SettingsOBody.GetViewModelFromModel(PatcherSettings.OBody, OBodySettingsVM, GeneralSettingsVM.RaceGroupings);

        RecordTemplatePlugins = SettingsIO_AssetPack.LoadRecordTemplates(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        RecordTemplateLinkCache = RecordTemplatePlugins.ToImmutableLinkCache();

        // load asset packs
        AssetPacks = SettingsIO_AssetPack.LoadAssetPacks(PatcherSettings.General.RaceGroupings, RecordTemplatePlugins, BodyGenConfigs, out loadSuccess); // load asset pack models from json
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_AssetPack.GetViewModelsFromModels(AssetPacks, PatcherSettings.TexMesh, this); // add asset pack view models to TexMesh shell view model here

        // load heights
        HeightConfigs = SettingsIO_Height.LoadHeightConfigs(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }

        VM_HeightConfig.GetViewModelsFromModels(HeightSettingsVM.AvailableHeightConfigs, HeightConfigs);
        VM_SettingsHeight.GetViewModelFromModel(HeightSettingsVM, PatcherSettings.Height); /// must do after populating configs
    }

    public void LoadFinalSettingsViewModels() // view models that should be loaded after plugin VMs because they depend on the loaded plugins
    {
        bool loadSuccess;
        // load specific assignments (must load after plugin view models)
        SpecificNPCAssignments = SettingsIO_SpecificNPCAssignments.LoadAssignments(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_SpecificNPCAssignmentsUI.GetViewModelFromModels(SpecificAssignmentsUIVM, SpecificNPCAssignments, OBodySettingsVM, GeneralSettingsVM, this);

        // Load Consistency (must load after plugin view models)
        Consistency = SettingsIO_Misc.LoadConsistency(out loadSuccess);
        if (!loadSuccess) { Logger.SwitchViewToLogDisplay(); }
        VM_ConsistencyUI.GetViewModelsFromModels(Consistency, ConsistencyUIVM.Assignments, TexMeshSettingsVM.AssetPacks);
    }

    public void SavePluginViewModels()
    {
        VM_AssetPack.DumpViewModelsToModels(TexMeshSettingsVM.AssetPacks, AssetPacks);
        VM_HeightConfig.DumpViewModelsToModels(HeightSettingsVM.AvailableHeightConfigs, HeightConfigs);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGenSettingsVM, PatcherSettings.BodyGen, BodyGenConfigs);
    }

    public void DumpViewModelsToModels()
    {
        VM_Settings_General.DumpViewModelToModel(GeneralSettingsVM, PatcherSettings.General);
        VM_SettingsTexMesh.DumpViewModelToModel(TexMeshSettingsVM, PatcherSettings.TexMesh);
        VM_AssetPack.DumpViewModelsToModels(TexMeshSettingsVM.AssetPacks, AssetPacks);
        VM_SettingsHeight.DumpViewModelToModel(HeightSettingsVM, PatcherSettings.Height);
        VM_HeightConfig.DumpViewModelsToModels(HeightSettingsVM.AvailableHeightConfigs, HeightConfigs);
        VM_SettingsBodyGen.DumpViewModelToModel(BodyGenSettingsVM, PatcherSettings.BodyGen, BodyGenConfigs);
        VM_SettingsOBody.DumpViewModelToModel(PatcherSettings.OBody, OBodySettingsVM);
        VM_SpecificNPCAssignmentsUI.DumpViewModelToModels(SpecificAssignmentsUIVM, SpecificNPCAssignments);
        VM_BlockListUI.DumpViewModelToModel(BlockListVM, BlockList);
        VM_ConsistencyUI.DumpViewModelsToModels(ConsistencyUIVM.Assignments, Consistency);
        VM_LinkedNPCGroup.DumpViewModelsToModels(LinkedNPCGroups, GeneralSettingsVM.LinkedNPCGroups);
        VM_SettingsModManager.DumpViewModelToModel(PatcherSettings.ModManagerIntegration, ModManagerSettingsVM);
    }

    void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        DumpViewModelsToModels();

        bool saveSuccess;
        string exceptionStr;
        string allExceptions = "";
        bool showFinalExceptions = false;

        JSONhandler<Settings_General>.SaveJSONFile(PatcherSettings.General, PatcherSettings.Paths.GeneralSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving General Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_TexMesh>.SaveJSONFile(PatcherSettings.TexMesh, PatcherSettings.Paths.TexMeshSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Texture and Mesh Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_AssetPack.SaveAssetPacks(AssetPacks, out saveSuccess);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_Height>.SaveJSONFile(PatcherSettings.Height, PatcherSettings.Paths.HeightSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Height Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_Height.SaveHeightConfigs(HeightConfigs, out saveSuccess);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_BodyGen>.SaveJSONFile(PatcherSettings.BodyGen, PatcherSettings.Paths.BodyGenSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving BodyGen Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }
            
        SettingsIO_BodyGen.SaveBodyGenConfigs(BodyGenConfigs.Female, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving BodyGen configs" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_BodyGen.SaveBodyGenConfigs(BodyGenConfigs.Male, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving BodyGen configs" + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_OBody>.SaveJSONFile(PatcherSettings.OBody, PatcherSettings.Paths.OBodySettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving OBody/AutoBody Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveConsistency(Consistency, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Consistency" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_SpecificNPCAssignments.SaveAssignments(SpecificNPCAssignments, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Specific NPC Assignmentss" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_BlockList.SaveBlockList(BlockList, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Block List" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveLinkedNPCGroups(LinkedNPCGroups, out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Linked NPC Groups" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveNPCNameExclusions(GeneralSettingsVM.LinkedNameExclusions.Select(cms => cms.Content).ToHashSet(), out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Linked NPC Name Exclusions" + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveTrimPaths(TexMeshSettingsVM.TrimPaths.ToHashSet(), out saveSuccess);
        if (!saveSuccess) { allExceptions += "Error saving Asset Path Trimming Settings" + Environment.NewLine; showFinalExceptions = true; }

        JSONhandler<Settings_ModManager>.SaveJSONFile(PatcherSettings.ModManagerIntegration, PatcherSettings.Paths.ModManagerSettingsPath, out saveSuccess, out exceptionStr);
        if (!saveSuccess) { Logger.LogMessage("Error saving Mod Manager Integration Settings: " + exceptionStr); allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        SettingsIO_Misc.SaveSettingsSource(out saveSuccess, out exceptionStr);
        if (!saveSuccess) { allExceptions += exceptionStr + Environment.NewLine; showFinalExceptions = true; }

        if (showFinalExceptions)
        {
            Logger.SwitchViewToLogDisplay();
            string notificationStr = allExceptions;
            CustomMessageBox.DisplayNotificationOK("Errors were encountered upon closing", notificationStr);
        }
    }

    void ValidateEval() // users should never see this but this will remind developer to update the Eval-Expression NuGet when the monthly trial expires
    {
        bool trueVar = false;
        try
        {
           trueVar = Z.Expressions.Eval.Execute<bool>("true == true");
        }
        catch
        {
            //pass
        }

        if (!trueVar)
        {
            CustomMessageBox.DisplayNotificationOK("Eval-Expression License Expired", "SynthEBD's asset distribution functionality depends on a month-to-month license of Eval-Expression.NET, and it appears this license has expired for the current build of SynthEBD. Please contact Piranha91 or another member of the Synthesis Collective to refresh this license by updating the Eval-Expression NuGet package. In the meantime, BodyGen, BodySlide, and Height distribution remain fully functional.");
        }
    }
}