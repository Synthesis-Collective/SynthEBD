using Autofac;
using static SynthEBD.VM_BodyShapeDescriptor;
using static SynthEBD.VM_ConfigPathRemapper;
using static SynthEBD.VM_NPCAttribute;

namespace SynthEBD;

/// <summary>
/// Autofac module that wires up the SynthEBD object graph: backend singletons (logging, IO
/// handlers, <see cref="PatcherState"/>, <see cref="SaveLoader"/>, <see cref="Patcher"/>,
/// path/update/validation helpers), the singleton main-UI view models plus transient
/// per-item view models, CharacterViewer host adapters bound behind the viewer's neutral
/// interfaces, and the patcher-stage components. Mode-specific providers (settings/environment
/// source, environment state) are registered by the individual <see cref="App"/> startup paths
/// rather than here.
/// </summary>
public class MainModule : Autofac.Module
{
    /// <summary>
    /// Registers all SynthEBD types with the container. Most backend and main-UI types are
    /// <c>SingleInstance</c>; per-item/DTO view models are transient. Also configures the
    /// <see cref="SynthEbdViewerHostStateRegistry"/> via a build callback that stores lazy
    /// resolver lambdas (so the not-yet-constructible environment provider is resolved on
    /// first viewer use rather than at container-build time).
    /// </summary>
    protected override void Load(ContainerBuilder builder)
    {
        // Singletons

        //logging
        builder.RegisterType<Logger>().AsSelf().SingleInstance();

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
        builder.RegisterType<SkyPatcherInterface>().AsSelf().SingleInstance();

        // UI components (main)
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<DisplayedItemVm>().AsSelf().SingleInstance();
        builder.RegisterType<VM_StatusBar>().AsSelf().SingleInstance();
        builder.RegisterType<VM_NavPanel>().AsSelf().SingleInstance();
        builder.RegisterType<VM_RunButton>().AsSelf().SingleInstance();
        builder.RegisterType<VM_Settings_General>().AsSelf().SingleInstance();
        builder.RegisterType<VM_NifPreviewNpcSettings>().AsSelf().SingleInstance();
        builder.RegisterType<PreviewNpcResolver>().AsSelf().SingleInstance();
        builder.RegisterType<SubgroupTextureMapper>().AsSelf().SingleInstance();
        builder.RegisterType<FaceGenPreviewService>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsTexMesh>().AsSelf().SingleInstance();
        builder.RegisterType<VM_ConfigEditor>().AsSelf().SingleInstance();
        builder.RegisterType<VM_SettingsDestandalone>().AsSelf().SingleInstance();
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
        builder.RegisterType<VM_DetailedReportNPCSelector>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyGenMiscMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyMiscSettings>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyTypeRegistry>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodyTypeProfileEditor>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyPreviewNpcSettings>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodySlidesMenu>().AsSelf().SingleInstance();
        builder.RegisterType<VM_BodySlideAnnotator>().AsSelf().SingleInstance();
        builder.RegisterType<VM_HeadPartImport>().AsSelf().SingleInstance();
        builder.RegisterType<ConfigDrafter>().AsSelf().SingleInstance();
        builder.RegisterType<VM_ConfigDrafter>().AsSelf().SingleInstance();
        builder.RegisterType<VM_AssetReplicateTextureRemover>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyTrainer>().AsSelf().SingleInstance();
        builder.RegisterType<VM_OBodyTrainerExporter>().AsSelf().SingleInstance();
        builder.RegisterType<VM_TexMeshBatchActions>().AsSelf().SingleInstance();

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

        // CharacterViewer host adapters — bind SynthEBD's concrete types behind the
        // modular CharacterViewer interfaces. The viewer subsystem only sees the
        // interfaces, so it can be reused by other host apps that supply their own
        // adapters. See CharacterViewerHost/ for the abstractions and adapters.
        builder.RegisterType<SynthEbdViewerLoggerAdapter>().As<ICharacterViewerLogger>().SingleInstance();
        builder.RegisterType<SynthEbdSettingsAdapter>().As<ICharacterViewerSettings>().SingleInstance();
        builder.RegisterType<SynthEbdDataFolderAdapter>().As<IDataFolderProvider>().SingleInstance();
        builder.RegisterType<SynthEbdBsaProviderAdapter>().As<IBsaArchiveProvider>().SingleInstance();
        builder.RegisterType<SynthEbdNpcMeshDataSourceAdapter>().As<INpcMeshDataSource>().SingleInstance();
        // Routes VM_CharacterViewer's pending-scene field writes to the WPF
        // dispatcher (UC_CharacterViewer's render callback runs on the UI
        // thread). The offscreen renderer constructs the VM with the default
        // InlineRenderThreadMarshaller instead — see Phase D.
        builder.RegisterType<WpfDispatcherMarshaller>().As<IRenderThreadMarshaller>().SingleInstance();

        builder.RegisterType<GameAssetResolver>().AsSelf().SingleInstance();
        builder.RegisterType<InstalledBodyTypeDetector>().AsSelf().SingleInstance();
        builder.RegisterType<NpcMeshResolver>().AsSelf().SingleInstance();
        // NifTextureLoader removed — textures now loaded by GlTextureManager via OpenGL pipeline
        builder.RegisterType<CharacterViewerLogGate>().AsSelf().SingleInstance();
        builder.RegisterType<BsdFileParser>().AsSelf().SingleInstance();
        builder.RegisterType<BodyTriFileParser>().AsSelf().SingleInstance();
        builder.RegisterType<BodySlideDeformer>().AsSelf().SingleInstance();
        // Shared NIF parse + NpcMeshPaths cache; survives viewer disposal so
        // BodySlide preset switches don't pay the parse/resolve cost twice.
        builder.RegisterType<CharacterPreviewCache>().AsSelf().SingleInstance();
        builder.RegisterType<VM_CharacterViewer>().AsSelf();

        // SynthEBD-side helpers for the viewer-host extension methods
        // (CharacterViewerSynthEbdExtensions). SynthEbdOsdLoader replaces the
        // viewer's old LoadOsdFilesForGroup; SynthEbdViewerHostStateRegistry
        // is the per-viewer state lookup the extension methods consult.
        //
        // Configure stores LAMBDAS rather than resolved instances: at build
        // time, SynthEbdOsdLoader's transitive dep IEnvironmentStateProvider
        // (StandaloneRunEnvironmentStateProvider in standalone mode) isn't
        // yet constructible because PatcherEnvironmentSourceProvider's
        // sourcePath argument isn't bound until the standalone bootstrap
        // completes. The lambdas resolve on first viewer construction, by
        // which time the environment is fully wired.
        builder.RegisterType<SynthEbdOsdLoader>().AsSelf().SingleInstance();
        builder.RegisterBuildCallback(c => SynthEbdViewerHostStateRegistry.Configure(
            () => c.Resolve<SynthEbdOsdLoader>(),
            () => c.Resolve<FaceGenPreviewService>(),
            () => c.Resolve<Logger>()));
        builder.RegisterType<RaceMenuIniHandler>().AsSelf().SingleInstance();
        builder.RegisterType<DictionaryMapper>().AsSelf().SingleInstance();
        builder.RegisterType<AliasHandler>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenPreprocessing>().AsSelf().SingleInstance();
        builder.RegisterType<OBodyPreprocessing>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartPreprocessing>().AsSelf().SingleInstance();
        builder.RegisterType<UniqueNPCData>().AsSelf().SingleInstance();

        builder.RegisterType<_7ZipInterface>().AsSelf().SingleInstance();
        builder.RegisterType<ConfigInstaller>().AsSelf().SingleInstance();
        builder.RegisterType<MiscValidation>().AsSelf().SingleInstance();
        builder.RegisterType<RecordGenerator>().AsSelf().SingleInstance();
        builder.RegisterType<RecordPathParser>().AsSelf().SingleInstance();
        builder.RegisterType<FileDialogs>().AsSelf().SingleInstance();
        builder.RegisterType<Converters>().AsSelf().SingleInstance();
        builder.RegisterType<AttributeMatcher>().AsSelf().SingleInstance();
        builder.RegisterType<RecordIntellisense>().AsSelf().SingleInstance();
        builder.RegisterType<AssetPackValidator>().AsSelf().SingleInstance();
        builder.RegisterType<BodySlideAnnotator>().AsSelf().SingleInstance();
        builder.RegisterType<AnnotationLibraryLoader>().AsSelf().SingleInstance();
        builder.RegisterType<AnnotationLibraryAnnotator>().AsSelf().SingleInstance();
        builder.RegisterType<BodySlideSettingMigrator>().AsSelf().SingleInstance();
        builder.RegisterType<SliderCatalogLoader>().AsSelf().SingleInstance();
        builder.RegisterType<BodySlideGroupClassifier>().AsSelf().SingleInstance();
        builder.RegisterType<BodyTypeFingerprintScanner>().AsSelf().SingleInstance();
        builder.RegisterType<BodyTypeSliderExtractor>().AsSelf().SingleInstance();
        builder.RegisterType<EasyNPCProfileParser>().AsSelf().SingleInstance();
        builder.RegisterType<NPC2ProfileParser>().AsSelf().SingleInstance();

        //Patcher components
        builder.RegisterType<AssetAndBodyShapeSelector>().AsSelf().SingleInstance();
        builder.RegisterType<AssetSelector>().AsSelf().SingleInstance();
        builder.RegisterType<AssetDistributionSimulator>().AsSelf().SingleInstance();
        builder.RegisterType<AssetReplacerSelector>().AsSelf().SingleInstance();
        builder.RegisterType<BodyShapeCandidateValidator>().AsSelf().SingleInstance();
        builder.RegisterType<BodyGenSelector>().AsSelf().SingleInstance();
        builder.RegisterType<OBodySelector>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartSelector>().AsSelf().SingleInstance();
        builder.RegisterType<HeightPatcher>().AsSelf().SingleInstance();
        builder.RegisterType<VanillaBodyPathSetter>().AsSelf().SingleInstance();
        builder.RegisterType<VerboseLoggingNPCSelector>().AsSelf().SingleInstance();
        builder.RegisterType<ArmorPatcher>().AsSelf().SingleInstance();
        builder.RegisterType<SkinPatcher>().AsSelf().SingleInstance();
        builder.RegisterType<HeadPartAuxFunctions>().AsSelf().SingleInstance();
        builder.RegisterType<SurrogateNPCProvider>().AsSelf().SingleInstance();
        builder.RegisterType<AssetAssignmentJsonDictHandler>().AsSelf().SingleInstance();
        builder.RegisterType<FacePartCompliance>().AsSelf().SingleInstance();
        builder.RegisterType<FaceGenPatcher>().AsSelf().SingleInstance();
        builder.RegisterType<MO2SourceResolver>().AsSelf().SingleInstance();
        builder.RegisterType<VortexSourceResolver>().AsSelf().SingleInstance();
        builder.RegisterType<SourceResolverProvider>().AsSelf().SingleInstance();

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
        builder.RegisterType<VM_NPCAttributeKeyword>().AsSelf();
        builder.RegisterType<VM_NPCAttributeMisc>().AsSelf();
        builder.RegisterType<VM_NPCAttributeMod>().AsSelf();
        builder.RegisterType<VM_NPCAttributeNPC>().AsSelf();
        builder.RegisterType<VM_NPCAttributeRace>().AsSelf();
        builder.RegisterType<VM_NPCAttributeVoiceType>().AsSelf();
        builder.RegisterType<VM_AttributeWeightModifier>().AsSelf();
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
        builder.RegisterType<VM_SpecificNPCAssignment.VM_MixInSpecificAssignment>().AsSelf();
        builder.RegisterType<VM_DrafterArchiveContainer>().AsSelf();
        builder.RegisterType<VM_7ZipInterface>().AsSelf();
        builder.RegisterType<VM_AdditionalRecordTemplate>().AsSelf();
        builder.RegisterType<VM_PositionalSubgroupContainerCollection>().AsSelf();
        builder.RegisterType<VM_ConfigPathRemapper>().AsSelf();
        builder.RegisterType<VM_ConfigInstaller>().AsSelf();
        builder.RegisterType<VM_DestinationFolderSelector>().AsSelf();
        builder.RegisterType<RemappedPath>().AsSelf();

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