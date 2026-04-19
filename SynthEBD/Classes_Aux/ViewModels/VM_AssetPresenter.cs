using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData.Binding;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Pfim;

namespace SynthEBD
{
    public class VM_AssetPresenter : VM
    {
        private readonly Logger _logger;
        private readonly VM_Settings_General _generalSettings;
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly SubgroupTextureMapper _textureMapper;

        public VM_AssetPresenter(
            VM_SettingsTexMesh parent,
            Logger logger,
            VM_Settings_General generalSettings,
            IEnvironmentStateProvider environmentProvider,
            SubgroupTextureMapper textureMapper,
            Func<VM_CharacterViewer> characterViewerFactory)
        {
            ParentUI = parent;
            _logger = logger;
            _generalSettings = generalSettings;
            _environmentProvider = environmentProvider;
            _textureMapper = textureMapper;

            CharacterViewer = characterViewerFactory();
            CharacterViewer.Mode = ViewerMode.ReadOnly;
            CharacterViewer.DisposeWith(this);

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            // Existing image-preview pipeline — still fires on Image mode.
            this.WhenAnyValue(
                    x => x.AssetPack.SelectedPlaceHolder,
                    x => x.ParentUI.PreviewMode,
                    x => x.AssetPack.SelectedPlaceHolder.ImagePreviewRefreshTrigger,
                    (_, _, _) => Unit.Default)
                .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Subscribe(_ => OnPreviewTriggerChanged())
                .DisposeWith(this);

            // AssetPack swap invalidates the accumulator and the override picker.
            this.WhenAnyValue(x => x.AssetPack)
                .Subscribe(_ =>
                {
                    AccumulatedOverrides.Clear();
                    PreviewNpcOverride = FormKey.Null;
                })
                .DisposeWith(this);

            // Live re-fire when the user picks a different preview NPC.
            this.WhenAnyValue(x => x.PreviewNpcOverride)
                .Skip(1) // skip initial default
                .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Subscribe(fk =>
                {
                    if (ParentUI.PreviewMode == PreviewMode.Render)
                    {
                        _ = RefreshRenderPreviewAsync();
                    }
                })
                .DisposeWith(this);

            SelectFromConfigFileCommand = new RelayCommand(
                canExecute: _ => ParentUI.PreviewMode == PreviewMode.Render && AssetPack != null,
                execute: _ =>
                {
                    if (AssetPack == null) return;
                    var packMap = _textureMapper.MapAssetPackTextures(AssetPack);
                    foreach (var kv in packMap)
                    {
                        AccumulatedOverrides[kv.Key] = kv.Value;
                    }
                    if (CharacterViewer.Renderer.Meshes.Count > 0)
                    {
                        CharacterViewer.ApplyTextureOverrides(AccumulatedOverrides.Values);
                    }
                });

            ResetAccumulatedOverridesCommand = new RelayCommand(
                canExecute: _ => ParentUI.PreviewMode == PreviewMode.Render,
                execute: _ =>
                {
                    AccumulatedOverrides.Clear();
                    _ = RefreshRenderPreviewAsync();
                });
        }

        public VM_SettingsTexMesh ParentUI { get; private set; }
        public VM_AssetPack AssetPack { get; set; }
        public ObservableCollection<VM_PreviewImage> PreviewImages { get; set; } = new();

        public VM_CharacterViewer CharacterViewer { get; }
        public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;
        public Dictionary<(string bodyPart, int slot), FilePathReplacement> AccumulatedOverrides { get; } = new();

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

        public RelayCommand SelectFromConfigFileCommand { get; }
        public RelayCommand ResetAccumulatedOverridesCommand { get; }

        private const ulong ByteLimit = 157286400; // minimum available RAM for image preview to function (in bytes)

        private void OnPreviewTriggerChanged()
        {
            switch (ParentUI.PreviewMode)
            {
                case PreviewMode.None:
                    ClearPreviewImages();
                    break;
                case PreviewMode.Image:
                    UpdatePreviewImages(AssetPack);
                    break;
                case PreviewMode.Render:
                    ClearPreviewImages();
                    _ = RefreshRenderPreviewAsync();
                    break;
            }
        }

        private async Task RefreshRenderPreviewAsync()
        {
            if (AssetPack == null || AssetPack.SelectedPlaceHolder == null || lk == null) return;

            var selected = AssetPack.SelectedPlaceHolder;

            try
            {
                var groupings = AssetPack.RaceGroupingEditor != null
                    ? AssetPack.RaceGroupingEditor.DumpToModel()
                    : new List<RaceGrouping>();
                var effectiveRaces = _textureMapper.ResolveEffectiveRaces(selected, groupings);
                var gender = SubgroupTextureMapper.DetermineGenderFromDestinations(selected.AssociatedModel.Paths);

                FormKey npc = PreviewNpcOverride.IsNull
                    ? _generalSettings.PreviewNpcs.ResolveNpc(effectiveRaces.FirstOrDefault(), gender)
                    : PreviewNpcOverride;

                if (npc.IsNull)
                {
                    _logger.LogMessage("VM_AssetPresenter: no preview NPC resolved for subgroup '" + selected.Name + "'");
                    return;
                }

                // LoadNpcAsync handles its own cancellation for rapid re-invocations.
                await CharacterViewer.LoadNpcAsync(npc, lk);

                // Merge this subgroup's own textures on top (last-seen per slot wins).
                var subgroupMap = _textureMapper.MapSubgroupTextures(selected);
                foreach (var kv in subgroupMap)
                {
                    AccumulatedOverrides[kv.Key] = kv.Value;
                }

                if (AccumulatedOverrides.Count > 0)
                {
                    CharacterViewer.ApplyTextureOverrides(AccumulatedOverrides.Values);
                }
            }
            catch (Exception ex)
            {
                _logger.LogMessage("VM_AssetPresenter.RefreshRenderPreviewAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
            }
        }

        public async void UpdatePreviewImages(VM_AssetPack source)
        {
            ClearPreviewImages(); // Try to free memory as completely as possible before loading more images

            try
            {
                if (source == null || source.DisplayedSubgroup == null || source.SelectedPlaceHolder == null) { return; }
                foreach (var sourcedImagePath in source.SelectedPlaceHolder.ImagePaths)
                {
                    var availableRAM = new Microsoft.VisualBasic.Devices.ComputerInfo().AvailablePhysicalMemory;
                    if (availableRAM <= ByteLimit) { continue; }
                    if (AssetPack.DisplayedSubgroup != null && sourcedImagePath.SourceChain != null & !sourcedImagePath.SourceChain.Contains(AssetPack.SelectedPlaceHolder)) { continue; } // stop loading images from a previous subgroup if a different one is selected

                    try
                    {
                        using (IImage image = await Task.Run(() => Pfimage.FromFile(sourcedImagePath.Path)))
                        {
                            if (image != null)
                            {
                                var bmp = ImagePreviewHandler.ResizeIImageAsBitMap(image, ParentUI.MaxPreviewImageSize);
                                var bmpSource = ImagePreviewHandler.CreateBitmapSourceFromGdiBitmap(bmp); // Try setting xaml to display bitmap directly
                                if (!sourcedImagePath.SourceChain.Contains(AssetPack.SelectedPlaceHolder)) { continue; } // Intentional duplication: Pfim.FromFile() takes some time to execute and may already be in progress when the user changes the active subgroup, leading to the last previous PreviewImage loading erroneously
                                PreviewImages.Add(new VM_PreviewImage(bmpSource, sourcedImagePath.PrimarySource));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        string errorStr = "Failed to load preview image from Subgroup " + sourcedImagePath.PrimarySource + " : " + sourcedImagePath.Path + Environment.NewLine + ExceptionLogger.GetExceptionStack(ex);
                        _logger.LogMessage(errorStr);
                    }
                }
            }
            catch
            {
                // fall through silently if user is spammy and triggers exception "Collection was modified; enumeration operation may not execute."
            }
            return;
        }

        private void ClearPreviewImages()
        {
            foreach (var i in PreviewImages)
            {
                i.Dispose();
            }
            PreviewImages.Clear();
            PreviewImages = new ObservableCollection<VM_PreviewImage>();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
