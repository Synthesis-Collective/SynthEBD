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
        builder.RegisterType<Logger>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);

        //IO
        builder.RegisterType<PatcherIO>().AsSelf().SingleInstance();
        builder.RegisterType<IO_Aux>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_General>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_AssetPack>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_BodyGen>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_OBody>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_SpecificNPCAssignments>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_BlockList>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_HeadParts>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_Height>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_ModManager>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsIO_Misc>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartWriter>().AsSelf().SingleInstance();
        builder.RegisterType<OBodyWriter>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenWriter>().AsSelf().SingleInstance();

        // UI components (main)
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<DisplayedItemVm>().AsSelf().SingleInstance();
        builder.RegisterType<VM_StatusBar>().AsSelf().SingleInstance();
        builder.RegisterType<VM_NavPanel>().AsSelf().SingleInstance();
        builder.RegisterType<VM_RunButton>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_General>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<VM_SettingsTexMesh>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<VM_SettingsBodyGen>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsOBody>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsHeight>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_Headparts>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<VM_SettingsModManager>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BlockListUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_ConsistencyUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SpecificNPCAssignmentsUI>().AsSelf().SingleInstance();        
        builder.RegisterType<VM_LogDisplay>().AsSelf().SingleInstance();
        builder.RegisterType<VM_AssetDistributionSimulator>().AsSelf().SingleInstance();
        
        // UI components (sub-menus)
        builder.RegisterType<VM_BodyGenTemplateMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyGenMiscMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyMiscSettings>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyShapeDescriptorCreationMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyShapeDescriptorSelectionMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_HeadPartImport>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);

        // UI Infrastructure
        builder.RegisterType<VM_NPCAttributeCreator>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyShapeDescriptorCreator>().AsSelf().SingleInstance();

        // Back End Infrastructure
        builder.RegisterType<PatcherSettingsSourceProvider>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<MainState>().AsSelf().SingleInstance();
        builder.RegisterType<Patcher>().AsSelf().SingleInstance();    
        builder.RegisterType<SaveLoader>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<PatcherEnvironmentProvider>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<SynthEBDPaths>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<UpdateHandler>().AsSelf().SingleInstance();
        builder.RegisterType<BSAHandler>().AsSelf().SingleInstance();
        builder.RegisterType<RaceMenuIniHandler>().AsSelf().SingleInstance();
        builder.RegisterType<DictionaryMapper>().AsSelf().SingleInstance();
        
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

        //Asset copiers
        builder.RegisterType<EBDScripts>().AsSelf().SingleInstance();
        builder.RegisterType<CommonScripts>().AsSelf().SingleInstance();
        builder.RegisterType<QuestInit>().AsSelf().SingleInstance();
        builder.RegisterType<JContainersDomain>().AsSelf().SingleInstance();

        //Misc / Deprecated
        builder.RegisterType<HardcodedRecordGenerator>().AsSelf().SingleInstance();

        // Non-singletons
        //UI
        builder.RegisterType<VM_AssetPack>().AsSelf();
        builder.RegisterType<VM_AssetPresenter>().AsSelf();
        builder.RegisterType<VM_Subgroup>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignment>().AsSelf();
        builder.RegisterType<VM_ConsistencyAssignment>().AsSelf();
        builder.RegisterType<VM_AttributeGroupMenu>().AsSelf();
        builder.RegisterType<VM_BodyGenConfig>().AsSelf();
        builder.RegisterType<VM_BodyGenTemplate>().AsSelf();
        builder.RegisterType<VM_HeightConfig>().AsSelf();
        builder.RegisterType<VM_HeadPart>().AsSelf();
        builder.RegisterType<VM_HeadPartList>().AsSelf();
        builder.RegisterType<VM_HeadPartCategoryRules>().AsSelf();
        builder.RegisterType<VM_ConfigDistributionRules>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptor>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorShell>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorCreationMenu>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorRules>().AsSelf();
        builder.RegisterType<VM_BodySlideSetting>().AsSelf();
        builder.RegisterType<VM_AssetPackDirectReplacerMenu>().AsSelf();
        builder.RegisterType<VM_AssetReplacerGroup>().AsSelf();
        builder.RegisterType<VM_FilePathReplacement>().AsSelf();
        builder.RegisterType<VM_NPCAttribute>().AsSelf();
        builder.RegisterType<VM_NPCAttributeShell>().AsSelf();
        builder.RegisterType<VM_NPCAttributeCustom>().AsSelf();
        builder.RegisterType<VM_AttributeValidator>().AsSelf();
        builder.RegisterType<VM_Manifest>().AsSelf();
        builder.RegisterType<VM_BlockedNPC>().AsSelf();

        //Non-UI
        builder.RegisterType<CombinationLog>().AsSelf();
        builder.RegisterType<NPCAttributeCustom>().AsSelf();
        builder.RegisterType<FlattenedAssetPack>().AsSelf();
        builder.RegisterType<FlattenedSubgroup>().AsSelf();
        builder.RegisterType<FlattenedReplacerGroup>().AsSelf();   
        builder.RegisterType<FilePathReplacementParsed>().AsSelf();
        builder.RegisterType<ZEBDAssetPack>().AsSelf();
        builder.RegisterType<ZEBDAssetPack.ZEBDSubgroup>().AsSelf();
        builder.RegisterType<BodyShapeDescriptorRules>().AsSelf();
    }
}