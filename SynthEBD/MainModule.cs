using Autofac;

namespace SynthEBD;

public class MainModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Singletons
        builder.RegisterType<Logger>().AsSelf().SingleInstance();
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<DisplayedItemVm>().AsSelf().SingleInstance();
        builder.RegisterType<VM_StatusBar>().AsSelf().SingleInstance();
        builder.RegisterType<VM_NavPanel>().AsSelf().SingleInstance();
        builder.RegisterType<VM_RunButton>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsTexMesh>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<VM_SettingsHeight>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsModManager>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BlockListUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsOBody>().AsSelf().SingleInstance();
        builder.RegisterType<VM_ConsistencyUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SpecificNPCAssignmentsUI>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsBodyGen>().AsSelf().SingleInstance();
        builder.RegisterType<VM_LogDisplay>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_General>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<MainState>().AsSelf().SingleInstance();
        builder.RegisterType<SaveLoader>().AsSelf().SingleInstance()
            .PropertiesAutowired(PropertyWiringOptions.AllowCircularDependencies);
        builder.RegisterType<PatcherEnvironmentProvider>().AsSelf().SingleInstance();
        builder.RegisterType<BSAHandler>().AsSelf().SingleInstance();
        builder.RegisterType<PatcherSettingsProvider>().AsSelf().SingleInstance();
        builder.RegisterType<Paths>().AsSelf().SingleInstance();
        
        // Other
        builder.RegisterType<VM_AssetPack>().AsSelf();
        builder.RegisterType<VM_SpecificNPCAssignment>().AsSelf();
        builder.RegisterType<VM_AttributeGroupMenu>().AsSelf();
        builder.RegisterType<VM_BodyGenConfig>().AsSelf();
        builder.RegisterType<AssetPackValidator>().AsSelf();
        builder.RegisterType<VM_Subgroup>().AsSelf();
        builder.RegisterType<Patcher>().AsSelf();
        builder.RegisterType<VM_BodyShapeDescriptorCreationMenu>().AsSelf();
        builder.RegisterType<CombinationLog>().AsSelf();
        builder.RegisterType<VM_AssetPackDirectReplacerMenu>().AsSelf();
        builder.RegisterType<VM_AssetReplacerGroup>().AsSelf();
        builder.RegisterType<VM_FilePathReplacement>().AsSelf();
    }
}