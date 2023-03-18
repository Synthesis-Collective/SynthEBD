using Autofac;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

public class MainModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Singletons

        //logging
        builder.RegisterType<Logger>().AsSelf().SingleInstance();
        builder.RegisterType<CustomMessageBox>().AsSelf().SingleInstance();

        //IO
        builder.RegisterType<PatcherIO>().AsSelf().SingleInstance();
        builder.RegisterType<IO_Aux>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_General>().AsSelf().SingleInstance().AsImplementedInterfaces();
        builder.RegisterType<SettingsIO_AssetPack>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_BodyGen>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_OBody>().AsSelf().SingleInstance().AsImplementedInterfaces();
        builder.RegisterType<SettingsIO_SpecificNPCAssignments>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_BlockList>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_HeadParts>().AsSelf().SingleInstance().AsImplementedInterfaces();
        builder.RegisterType<SettingsIO_Height>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_ModManager>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_Misc>().AsSelf().SingleInstance();
        builder.RegisterType<FaceTextureScriptWriter>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartWriter>().AsSelf().SingleInstance();
        builder.RegisterType<OBodyWriter>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenWriter>().AsSelf().SingleInstance();
        builder.RegisterType<FirstLaunch>().AsSelf().SingleInstance();

        // UI components (main)
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<DisplayedItemVm>().AsSelf().SingleInstance();
        builder.RegisterType<VM_StatusBar>().AsSelf().SingleInstance();
        builder.RegisterType<VM_NavPanel>().AsSelf().SingleInstance();
        builder.RegisterType<VM_RunButton>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_General>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsTexMesh>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsBodyGen>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsOBody>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsHeight>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_Headparts>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsModManager>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BlockListUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_ConsistencyUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SpecificNPCAssignmentsUI>().AsSelf().SingleInstance();        
        builder.RegisterType<VM_LogDisplay>().AsSelf().SingleInstance();
        builder.RegisterType<VM_AssetDistributionSimulator>().AsSelf().SingleInstance();
        
        // UI components (sub-menus)
        builder.RegisterType<VM_BodyGenMiscMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyMiscSettings>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodySlidesMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_HeadPartImport>().AsSelf().SingleInstance();

        // UI Infrastructure
        builder.RegisterType<VM_NPCAttributeCreator>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyShapeDescriptorCreator>().AsSelf().SingleInstance();

        // Back End Infrastructure
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherState>().AsSelf().SingleInstance();
        builder.RegisterType<Patcher>().AsSelf().SingleInstance();
        builder.RegisterType<SaveLoader>().AsSelf().SingleInstance();
        builder.RegisterType<ViewModelLoader>().AsSelf().SingleInstance();
        builder.RegisterType<PatchableRaceResolver>().AsSelf().SingleInstance();
        builder.RegisterType<PreRunValidation>().AsSelf().SingleInstance();
        builder.RegisterType<SynthEBDPaths>().AsSelf().SingleInstance();
        builder.RegisterType<UpdateHandler>().AsSelf().SingleInstance();
        builder.RegisterType<BSAHandler>().AsSelf().SingleInstance();
        builder.RegisterType<RaceMenuIniHandler>().AsSelf().SingleInstance();
        builder.RegisterType<DictionaryMapper>().AsSelf().SingleInstance();
        builder.RegisterType<AliasHandler>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenPreprocessing>().AsSelf().SingleInstance();
        builder.RegisterType<OBodyPreprocessing>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartPreprocessing>().AsSelf().SingleInstance();
        
        builder.RegisterType<ConfigInstaller>().AsSelf().SingleInstance();
        builder.RegisterType<MiscValidation>().AsSelf().SingleInstance();
        builder.RegisterType<RecordGenerator>().AsSelf().SingleInstance();
        builder.RegisterType<RecordPathParser>().AsSelf().SingleInstance();
        builder.RegisterType<FileDialogs>().AsSelf().SingleInstance();
        builder.RegisterType<Converters>().AsSelf().SingleInstance();
        builder.RegisterType<AttributeMatcher>().AsSelf().SingleInstance();
        builder.RegisterType<RecordIntellisense>().AsSelf().SingleInstance();
        builder.RegisterType<AssetPackValidator>().AsSelf().SingleInstance();

        //Patcher components
        builder.RegisterType<AssetAndBodyShapeSelector>().AsSelf().SingleInstance();
        builder.RegisterType<AssetSelector>().AsSelf().SingleInstance();
        builder.RegisterType<AssetReplacerSelector>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenSelector>().AsSelf().SingleInstance();
        builder.RegisterType<OBodySelector>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartSelector>().AsSelf().SingleInstance();
        builder.RegisterType<HeightPatcher>().AsSelf().SingleInstance();
        builder.RegisterType<VanillaBodyPathSetter>().AsSelf().SingleInstance();

        //Asset copiers
        builder.RegisterType<EBDScripts>().AsSelf().SingleInstance();
        builder.RegisterType<CommonScripts>().AsSelf().SingleInstance();
        builder.RegisterType<QuestInit>().AsSelf().SingleInstance();
        builder.RegisterType<JContainersDomain>().AsSelf().SingleInstance();

        //Misc / Deprecated
        builder.RegisterType<HardcodedRecordGenerator>().AsSelf().SingleInstance();

        // Non-singletons
        //UI
        builder.RegisterType<VM_AssetPack>().AsSelf().AsImplementedInterfaces();
        builder.RegisterType<VM_AssetPresenter>().AsSelf();
        builder.RegisterType<VM_AssetPackMiscMenu>().AsSelf();
        builder.RegisterType<VM_AssetPackDirectReplacerMenu>().AsSelf();
        builder.RegisterType<VM_AssetReplacerGroup>().AsSelf();
        builder.RegisterType<VM_SubgroupPlaceHolder>().AsSelf();
        builder.RegisterType<VM_Subgroup>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignment>().AsSelf();
        builder.RegisterType<VM_ConsistencyAssignment>().AsSelf();
        builder.RegisterType<VM_AttributeGroupMenu>().AsSelf();
        builder.RegisterType<VM_AttributeGroup>().AsSelf();
        builder.RegisterType<VM_BodyGenConfig>().AsSelf();
        builder.RegisterType<VM_BodyGenGroupMappingMenu>().AsSelf();
        builder.RegisterType<VM_BodyGenRacialMapping>().AsSelf();
        builder.RegisterType<VM_BodyGenTemplateMenu>().AsSelf();
        builder.RegisterType<VM_BodyGenTemplate>().AsSelf();
        builder.RegisterType<VM_BodySlideExchange>().AsSelf();
        builder.RegisterType<VM_HeightConfig>().AsSelf();
        builder.RegisterType<VM_HeightAssignment>().AsSelf();
        builder.RegisterType<VM_HeadPartPlaceHolder>().AsSelf();
        builder.RegisterType<VM_HeadPart>().AsSelf();
        builder.RegisterType<VM_HeadPartList>().AsSelf();
        builder.RegisterType<VM_HeadPartAssignment>().AsSelf();
        builder.RegisterType<VM_HeadPartCategoryRules>().AsSelf();
        builder.RegisterType<VM_ConfigDistributionRules>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptor>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorShell>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorRules>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorCreationMenu>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorSelectionMenu>().AsSelf();
        builder.RegisterType<VM_BodySlidePlaceHolder>().AsSelf();
        builder.RegisterType<VM_BodySlideSetting>().AsSelf();
        builder.RegisterType<VM_FilePathReplacementMenu>().AsSelf();
        builder.RegisterType<VM_FilePathReplacement>().AsSelf();
        builder.RegisterType<VM_NPCAttribute>().AsSelf();
        builder.RegisterType<VM_NPCAttributeShell>().AsSelf();
        builder.RegisterType<VM_NPCAttributeClass>().AsSelf();
        builder.RegisterType<VM_NPCAttributeCustom>().AsSelf();
        builder.RegisterType<VM_NPCAttributeFaceTexture>().AsSelf();
        builder.RegisterType<VM_NPCAttributeFactions>().AsSelf();
        builder.RegisterType<VM_NPCAttributeGroup>().AsSelf();
        builder.RegisterType<VM_NPCAttributeMisc>().AsSelf();
        builder.RegisterType<VM_NPCAttributeMod>().AsSelf();
        builder.RegisterType<VM_NPCAttributeNPC>().AsSelf();
        builder.RegisterType<VM_NPCAttributeRace>().AsSelf();
        builder.RegisterType<VM_NPCAttributeVoiceType>().AsSelf();
        builder.RegisterType<VM_AttributeValidator>().AsSelf();
        builder.RegisterType<VM_Manifest>().AsSelf();
        builder.RegisterType<VM_BlockedNPCPlaceHolder>().AsSelf();
        builder.RegisterType<VM_BlockedNPC>().AsSelf();
        builder.RegisterType<VM_BlockedPluginPlaceHolder>().AsSelf();
        builder.RegisterType<VM_BlockedPlugin>().AsSelf();
        builder.RegisterType<VM_RaceGroupingEditor>().AsSelf();
        builder.RegisterType<VM_RaceGrouping>().AsSelf();
        builder.RegisterType<VM_RaceAlias>().AsSelf();
        builder.RegisterType<VM_LinkedNPCGroup>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignmentPlaceHolder>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignment>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignment.VM_MixInSpecificAssignment>().AsSelf();

        // DTOs with factories
        builder.RegisterType<CombinationLog>().AsSelf();
        builder.RegisterType<FlattenedAssetPack>().AsSelf();
        builder.RegisterType<FlattenedSubgroup>().AsSelf();
        builder.RegisterType<FlattenedReplacerGroup>().AsSelf();   
        builder.RegisterType<FilePathReplacementParsed>().AsSelf();

        // Internal types
        builder.RegisterType<NPCInfo>().AsSelf();

        //Misc
        builder.RegisterType<Mutagen.Bethesda.Skyrim.HeadPart>().AsSelf();
    }
}