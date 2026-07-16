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
    /// <summary>
    /// View model for the asset-preview pane: drives either the image-preview pipeline or the 3D
    /// CharacterViewer render preview for the selected subgroup, accumulating texture overrides and
    /// resolving a suitable preview NPC.
    /// </summary>
    public class VM_AssetPresenter : VM
    {
        private readonly Logger _logger;
        private readonly VM_Settings_General _generalSettings;
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly SubgroupTextureMapper _textureMapper;
        private readonly AssetDistributionSimulator _simulator;
        private readonly PatcherState _patcherState;

        /// <summary>Creates the presenter, building the read-only character viewer and wiring preview-mode/selection/NPC-override subscriptions plus the select-from-config and reset commands.</summary>
        /// <param name="parent">The owning texture/mesh settings VM.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="generalSettings">General settings (preview-NPC resolution).</param>
        /// <param name="environmentProvider">Supplies the link cache.</param>
        /// <param name="textureMapper">Maps subgroup/pack textures and resolves effective races.</param>
        /// <param name="simulator">Distribution simulator used to roll a valid subgroup combination for the preview.</param>
        /// <param name="patcherState">Supplies the BodyGen/OBody/BlockList settings the simulator needs.</param>
        /// <param name="characterViewerFactory">Factory for the embedded 3D character viewer.</param>
        public VM_AssetPresenter(
            VM_SettingsTexMesh parent,
            Logger logger,
            VM_Settings_General generalSettings,
            IEnvironmentStateProvider environmentProvider,
            SubgroupTextureMapper textureMapper,
            AssetDistributionSimulator simulator,
            PatcherState patcherState,
            Func<VM_CharacterViewer> characterViewerFactory)
        {
            ParentUI = parent;
            _logger = logger;
            _generalSettings = generalSettings;
            _environmentProvider = environmentProvider;
            _textureMapper = textureMapper;
            _simulator = simulator;
            _patcherState = patcherState;

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
                    AccumulatedMeshOverrides.Clear();
                    PreviewNpcOverride = FormKey.Null;
                    _lastLoadedNpc = FormKey.Null;
                    ClearMeshOverrideWarning();
                })
                .DisposeWith(this);

            // Mesh overrides drain after the scene commits when a load was in
            // flight at apply time; refresh the missing-asset warning then so it
            // reflects the freshly-synthesized shapes (e.g. a skipped or
            // misaligned auxiliary mesh).
            void OnSceneCommitted() => UpdateMeshOverrideWarning();
            CharacterViewer.SceneCommitted += OnSceneCommitted;
            System.Reactive.Disposables.Disposable
                .Create(() => CharacterViewer.SceneCommitted -= OnSceneCommitted)
                .DisposeWith(this);

            // Live re-fire when the user picks a different preview NPC or toggles the lock.
            this.WhenAnyValue(x => x.PreviewNpcOverride, x => x.LockPreviewNpc)
                .Skip(1) // skip initial default
                .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Subscribe(tuple =>
                {
                    if (ParentUI.PreviewMode == PreviewMode.Render)
                    {
                        _ = RefreshRenderPreviewAsync();
                    }
                })
                .DisposeWith(this);

            // Live re-fire when the config file's declared gender changes, so an open
            // render preview immediately swaps to an NPC of the new gender (Render mode
            // only — image previews don't depend on gender).
            this.WhenAnyValue(x => x.AssetPack.Gender)
                .Skip(1) // skip the initial value pushed at subscription time
                .Throttle(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Subscribe(gender =>
                {
                    if (ParentUI.PreviewMode == PreviewMode.Render)
                    {
                        _ = RefreshRenderPreviewAsync();
                    }
                })
                .DisposeWith(this);

            SelectFromConfigFileCommand = new RelayCommand(
                canExecute: _ => ParentUI.PreviewMode == PreviewMode.Render && AssetPack != null,
                execute: _ => { var _unused = SelectCombinationFromConfigAsync(); });

            ResetAccumulatedOverridesCommand = new RelayCommand(
                canExecute: _ => ParentUI.PreviewMode == PreviewMode.Render,
                execute: _ =>
                {
                    AccumulatedOverrides.Clear();
                    AccumulatedMeshOverrides.Clear();
                    _ = RefreshRenderPreviewAsync();
                });
        }

        public VM_SettingsTexMesh ParentUI { get; private set; }
        public VM_AssetPack AssetPack { get; set; }
        public ObservableCollection<VM_PreviewImage> PreviewImages { get; set; } = new();

        public VM_CharacterViewer CharacterViewer { get; }
        public FormKey PreviewNpcOverride { get; set; } = FormKey.Null;
        public bool LockPreviewNpc { get; set; } = false;
        private FormKey _lastLoadedNpc = FormKey.Null;
        public Dictionary<SubgroupTextureMapper.OverrideKey, TextureOverride> AccumulatedOverrides { get; } = new();

        /// <summary>Accumulated auxiliary mesh overrides, keyed by the biped-slot
        /// bitmask they occupy so a later selection replaces only the same-slot
        /// variant. Mirrors <see cref="AccumulatedOverrides"/> for textures:
        /// once a subgroup contributes an auxiliary mesh it persists across other
        /// subgroup selections until a different variant for that slot is chosen
        /// (or Reset / an asset-pack swap clears it).</summary>
        public Dictionary<int, MeshOverride> AccumulatedMeshOverrides { get; } = new();

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> NPCPickerFormKeys { get; } = typeof(INpcGetter).AsEnumerable();

        /// <summary>True when one or more auxiliary mesh overrides for the
        /// current selection couldn't be rendered (the override NIF didn't
        /// resolve, or a shape is weighted to a bone in neither the skeleton nor
        /// the mesh), or rendered but against an incompatible skeleton (bones the
        /// mesh expects are missing from the loaded skeleton, so it may be
        /// misaligned — e.g. a required skeleton mod isn't installed). Drives the
        /// warning line under the render preview, mirroring NPC Plugin Chooser
        /// 2's mugshot missing-asset icon.</summary>
        public bool ShowMeshOverrideWarning { get; set; }

        /// <summary>Human-readable detail of why the auxiliary mesh override(s)
        /// were skipped or may be misaligned; shown as the warning line's
        /// tooltip / text.</summary>
        public string MeshOverrideWarning { get; set; } = "";

        public RelayCommand SelectFromConfigFileCommand { get; }
        public RelayCommand ResetAccumulatedOverridesCommand { get; }

        /// <summary>Re-entrancy guard for <see cref="SelectCombinationFromConfigAsync"/>:
        /// the roll now runs on a background thread, so the button stays clickable while
        /// one is in flight; a second click is ignored instead of racing the first.</summary>
        private bool _selectFromConfigInFlight;

        private const ulong ByteLimit = 157286400; // minimum available RAM for image preview to function (in bytes)

        /// <summary>Reacts to a preview trigger by clearing/loading image previews or refreshing the 3D render, per the current preview mode.</summary>
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

        /// <summary>Resolves the effective races and preview NPC for the selected subgroup — using the asset pack's configured <see cref="VM_AssetPack.Gender"/> rather than a per-subgroup destination heuristic — loads it into the character viewer, and applies the accumulated texture overrides.</summary>
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
                // The config file declares its gender (VM_AssetPack.Gender), so drive the
                // preview NPC off that directly — a male config shows a male NPC immediately,
                // without waiting for a male-specific subgroup to be selected.
                var gender = AssetPack.Gender;

                FormKey npc;
                if (!PreviewNpcOverride.IsNull)
                {
                    npc = PreviewNpcOverride;
                    _lastLoadedNpc = npc;
                }
                else if (LockPreviewNpc && !_lastLoadedNpc.IsNull)
                {
                    npc = _lastLoadedNpc;
                }
                else
                {
                    npc = _generalSettings.PreviewNpcs.ResolveNpc(effectiveRaces.FirstOrDefault(), gender);
                    _lastLoadedNpc = npc;
                }

                if (npc.IsNull)
                {
                    _logger.LogMessage("VM_AssetPresenter: no preview NPC resolved for subgroup '" + selected.Name + "'");
                    return;
                }

                // LoadNpcAsync handles its own cancellation for rapid re-invocations.
                await CharacterViewer.LoadNpcAsync(npc, lk);

                // Merge this subgroup's own textures on top (last-seen per key wins).
                // Every destination is resolved against the pack's record template (and the
                // preview NPC as a fallback) so the target body part / slots come from the real
                // armature, and worn-armor AlternateTextures target their named sub-shape.
                var resolutionContext = BuildResolutionContext(npc);
                var subgroupMap = _textureMapper.MapSubgroupTextures(selected, resolutionContext);
                foreach (var kv in subgroupMap)
                {
                    AccumulatedOverrides[kv.Key] = kv.Value;
                }

                if (AccumulatedOverrides.Count > 0)
                {
                    CharacterViewer.ApplyTextureOverrides(AccumulatedOverrides.Values);
                }

                // Merge any non-base armature mesh the selection defines (an
                // auxiliary armature on a free slot, e.g. slot 52) into the
                // accumulator, keyed by slot so a later selection replaces only
                // the same-slot variant. Like the texture accumulator above, the
                // mesh persists across other subgroup selections until a
                // different variant for that slot is chosen (or Reset clears it).
                foreach (var mo in _textureMapper.MapSubgroupMeshOverrides(selected, resolutionContext))
                {
                    AccumulatedMeshOverrides[mo.BipedSlots] = mo;
                }
                CharacterViewer.ApplyMeshOverrides(AccumulatedMeshOverrides.Values);
                UpdateMeshOverrideWarning();
            }
            catch (Exception ex)
            {
                _logger.LogMessage("VM_AssetPresenter.RefreshRenderPreviewAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
            }
        }

        /// <summary>
        /// Backs the "Select from Config File" button: rolls one distribution-rule-valid
        /// subgroup combination via <see cref="AssetDistributionSimulator"/> and renders it.
        /// <para>When a preview NPC is already chosen in the picker, a single random
        /// combination compatible with that NPC is rolled. When none is chosen, the
        /// configured preview-NPC defaults (General Settings) are tried in turn until one
        /// is compatible with the config's distribution rules; the first hit is loaded.
        /// In both cases the rolled combination's textures and auxiliary meshes — not the
        /// whole pack's assets — are applied, so the preview matches what the patcher would
        /// actually produce.</para>
        /// </summary>
        private async Task SelectCombinationFromConfigAsync()
        {
            if (AssetPack == null || lk == null) return;
            if (_selectFromConfigInFlight) return;
            _selectFromConfigInFlight = true;

            // Busy overlay covers the whole operation: IsHostBusy spans the background
            // roll (cleared in finally), and once LoadNpcAsync starts, the viewer's own
            // IsLoading takes over through GL scene commit.
            CharacterViewer.HostBusyMessage = "Selecting from config...";
            CharacterViewer.IsHostBusy = true;

            try
            {
                // VM reads stay on the UI thread; only model-level work moves off it.
                var packModel = AssetPack.DumpViewModelToModel();
                var gender = AssetPack.Gender;
                var previewOverride = PreviewNpcOverride;
                var groupName = AssetPack.GroupName;

                FormKey chosenNpc;
                SubgroupCombination? combination;

                if (!previewOverride.IsNull)
                {
                    // An NPC is already selected: roll a single random compatible combination
                    // for it. The simulator walks the whole distribution pipeline, which can
                    // take seconds on large configs — run it off the UI thread so the overlay
                    // animates instead of the window freezing.
                    string reason = string.Empty;
                    combination = await Task.Run(() => TryRollCombination(previewOverride, packModel, out reason));
                    if (combination == null)
                    {
                        CharacterViewer.IsHostBusy = false; // stop the spinner behind the modal
                        MessageWindow.DisplayNotificationOK("No compatible assignment",
                            "The config file '" + groupName + "' can't be assigned to the selected preview NPC under its current distribution rules."
                            + (string.IsNullOrWhiteSpace(reason) ? "" : Environment.NewLine + Environment.NewLine + reason));
                        return;
                    }
                    chosenNpc = previewOverride;
                }
                else
                {
                    // No NPC selected: iterate the configured preview-NPC defaults until one
                    // yields a valid combination under the config's distribution rules.
                    // Candidates are materialized here because the enumeration reads
                    // General-settings VM rows, which must not be touched off-thread.
                    var candidates = EnumeratePreviewNpcCandidates(gender).ToList();
                    (chosenNpc, combination) = await Task.Run(() =>
                    {
                        foreach (var candidate in candidates)
                        {
                            var rolled = TryRollCombination(candidate, packModel, out _);
                            if (rolled != null) { return (candidate, rolled); }
                        }
                        return (FormKey.Null, (SubgroupCombination?)null);
                    });
                    if (combination == null)
                    {
                        CharacterViewer.IsHostBusy = false; // stop the spinner behind the modal
                        MessageWindow.DisplayNotificationOK("No compatible preview NPC",
                            "None of the configured preview NPCs for " + gender + " could be assigned assets from '"
                            + groupName + "' under its current distribution rules. "
                            + "Pick a specific Preview NPC, or review the config's distribution rules with the Distribution Simulator.");
                        return;
                    }
                }

                // Load the chosen NPC, then apply the rolled combination's assets.
                await CharacterViewer.LoadNpcAsync(chosenNpc, lk);
                _lastLoadedNpc = chosenNpc;

                var resolutionContext = BuildResolutionContext(chosenNpc);
                AccumulatedOverrides.Clear();
                foreach (var kv in _textureMapper.MapCombinationTextures(combination, resolutionContext))
                {
                    AccumulatedOverrides[kv.Key] = kv.Value;
                }
                if (AccumulatedOverrides.Count > 0)
                {
                    CharacterViewer.ApplyTextureOverrides(AccumulatedOverrides.Values);
                }

                AccumulatedMeshOverrides.Clear();
                foreach (var mo in _textureMapper.MapCombinationMeshOverrides(combination, resolutionContext))
                {
                    AccumulatedMeshOverrides[mo.BipedSlots] = mo;
                }
                CharacterViewer.ApplyMeshOverrides(AccumulatedMeshOverrides.Values);
                UpdateMeshOverrideWarning();

                _logger.LogMessage("VM_AssetPresenter: rolled combination '" + combination.Signature
                    + "' from '" + AssetPack.GroupName + "' for preview NPC " + chosenNpc);
            }
            catch (Exception ex)
            {
                _logger.LogMessage("VM_AssetPresenter.SelectCombinationFromConfigAsync failed: " + ExceptionLogger.GetExceptionStack(ex));
            }
            finally
            {
                CharacterViewer.IsHostBusy = false;
                _selectFromConfigInFlight = false;
            }
        }

        /// <summary>
        /// Assembles the record-path resolution context for a preview NPC via
        /// <see cref="SubgroupTextureMapper.BuildContext"/>: the pack's race-specific record-template NPC as
        /// the primary root and the loaded preview NPC (with the live environment link cache) as a fallback,
        /// so every destination is resolved against a real record rather than pattern-matched from its string.
        /// </summary>
        private SubgroupTextureMapper.DestinationResolutionContext BuildResolutionContext(FormKey npc)
        {
            INpcGetter? previewNpc = null;
            if (!npc.IsNull && lk != null)
            {
                lk.TryResolve<INpcGetter>(npc, out previewNpc);
            }
            return SubgroupTextureMapper.BuildContext(AssetPack, previewNpc, lk);
        }

        /// <summary>
        /// Runs one repetition of the asset-distribution pipeline for <paramref name="npcFormKey"/>
        /// against the single supplied pack, returning a random valid <see cref="SubgroupCombination"/>
        /// (or null with <paramref name="failureReason"/> when the NPC is unresolvable, the pack's
        /// gender doesn't match, or the distribution rules exclude the NPC entirely).
        /// </summary>
        private SubgroupCombination? TryRollCombination(FormKey npcFormKey, AssetPack packModel, out string failureReason)
        {
            failureReason = string.Empty;
            if (npcFormKey.IsNull) { failureReason = "No NPC supplied."; return null; }
            if (lk == null || !lk.TryResolve<INpcGetter>(npcFormKey, out var npcGetter))
            {
                failureReason = "Preview NPC " + npcFormKey + " could not be resolved in the current load order.";
                return null;
            }

            var result = _simulator.SimulatePrimaryDistribution(
                npcGetter,
                new HashSet<AssetPack> { packModel },
                _patcherState.BodyGenConfigs,
                _patcherState.OBodySettings,
                _patcherState.BlockList,
                1,
                out failureReason);

            return result?.Combinations.FirstOrDefault(c => c.AssetPack != null);
        }

        /// <summary>
        /// Ordered, de-duplicated preview-NPC candidates for <paramref name="gender"/>, drawn from
        /// General Settings: the Default pair first ("the defaults"), then every per-race row's NPC
        /// ("the options"). Null entries are skipped.
        /// </summary>
        private IEnumerable<FormKey> EnumeratePreviewNpcCandidates(Gender gender)
        {
            var preview = _generalSettings?.PreviewNpcs;
            if (preview == null) yield break;

            var seen = new HashSet<FormKey>();

            FormKey defaultNpc = gender == Gender.Female ? preview.DefaultRow?.FemaleNpc ?? FormKey.Null
                                                         : preview.DefaultRow?.MaleNpc ?? FormKey.Null;
            if (!defaultNpc.IsNull && seen.Add(defaultNpc)) yield return defaultNpc;

            foreach (var row in preview.Rows)
            {
                FormKey rowNpc = gender == Gender.Female ? row.FemaleNpc : row.MaleNpc;
                if (!rowNpc.IsNull && seen.Add(rowNpc)) yield return rowNpc;
            }
        }

        /// <summary>Reflects <see cref="VM_CharacterViewer.MeshOverrideWarnings"/>
        /// onto the bound warning line. Called after applying overrides and again
        /// when the scene commits (the apply may have been queued behind a load).
        /// Covers both unrenderable meshes and meshes that rendered against an
        /// incompatible / missing skeleton.</summary>
        private void UpdateMeshOverrideWarning()
        {
            var warnings = CharacterViewer?.MeshOverrideWarnings;
            if (warnings == null || warnings.Count == 0)
            {
                ClearMeshOverrideWarning();
                return;
            }
            MeshOverrideWarning = "Some auxiliary preview meshes couldn't be rendered correctly "
                + "(mesh not found, missing skinning bones, or an incompatible/absent skeleton):"
                + Environment.NewLine
                + " - " + string.Join(Environment.NewLine + " - ", warnings);
            ShowMeshOverrideWarning = true;
        }

        /// <summary>Hides the mesh-override warning line.</summary>
        private void ClearMeshOverrideWarning()
        {
            ShowMeshOverrideWarning = false;
            MeshOverrideWarning = "";
        }

        /// <summary>Loads the selected subgroup's preview images (respecting an available-RAM floor and abandoning stale loads when the selection changes).</summary>
        /// <param name="source">The asset pack whose selected subgroup's images are loaded.</param>
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
                    if (AssetPack.DisplayedSubgroup != null && sourcedImagePath.SourceChain != null && !sourcedImagePath.SourceChain.Contains(AssetPack.SelectedPlaceHolder)) { continue; } // stop loading images from a previous subgroup if a different one is selected

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

        /// <summary>Disposes and clears the current preview images and forces a GC pass to release their (often large) memory.</summary>
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
