using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Runs all pre-patch validation: verifies the external mods/tools required by each enabled feature
    /// (EBD, RaceMenu, OBody/AutoBody, JContainers, SkyPatcher, PO3 for VR, …), validates the selected
    /// asset packs and generated BodySlide/BodyGen data, checks attribute completeness, and rejects the
    /// invalid asset/headpart mode combinations from the patcher's 16-case truth table.
    /// </summary>
    public class PreRunValidation
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly MiscValidation _miscValidation;
        private readonly AssetPackValidator _assetPackValidator;
        /// <summary>Creates the validator.</summary>
        /// <param name="environmentProvider">Supplies the Skyrim version and environment.</param>
        /// <param name="patcherState">The settings/state to validate.</param>
        /// <param name="logger">Logger for surfacing validation errors.</param>
        /// <param name="miscValidation">Per-dependency installation/data checks.</param>
        /// <param name="assetPackValidator">Validator for individual asset packs.</param>
        public PreRunValidation(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, MiscValidation miscValidation, AssetPackValidator assetPackValidator)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _miscValidation = miscValidation;
            _assetPackValidator = assetPackValidator;
        }

        /// <summary>Validates the entire patcher configuration before a run, logging every problem found.</summary>
        /// <returns><c>true</c> if the configuration is valid (or validation is disabled); otherwise <c>false</c>, with errors logged.</returns>
        /// <remarks>
        /// Short-circuits to <c>true</c> when validation is disabled in settings. Otherwise checks, per enabled
        /// feature: required runtime mods/tools, asset-pack validity, generated BodySlide/BodyGen <c>.tri</c>
        /// files, unique BodySlide labels, and blank NPC attributes. Finally rejects the invalid asset/headpart
        /// mode combinations from the 16-case truth table documented in <c>Patcher.cs</c>.
        /// </remarks>
        public bool ValidatePatcherState()
        {
            bool valid = true;

            if (_patcherState.GeneralSettings.bDisableValidation)
            {
                _logger.LogMessage("Pre-run validation is disabled");
                return true;
            }

            if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
            {
                if (!_miscValidation.VerifyEBDInstalled())
                {
                    valid = false;
                }
                
                if (_environmentProvider.SkyrimVersion == Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimVR && _patcherState.TexMeshSettings.bPO3ModeForVR && !(_miscValidation.VerifyPO3ExtenderInstalled() && _miscValidation.VerifyPO3TweaksInstalled()))
                {
                    valid = false;
                }
                
                if (_patcherState.TexMeshSettings.bSkyPatcherModeAssets && !_miscValidation.VerifySkyPatcherInstalled(false))
                {
                    valid = false;
                }

                List<string> assetPackErrors = new();
                foreach (var assetPack in _patcherState.AssetPacks.Where(x => _patcherState.TexMeshSettings.SelectedAssetPacks.Contains(x.GroupName)).ToArray())
                {
                    if (!_assetPackValidator.Validate(assetPack, assetPackErrors, _patcherState.BodyGenConfigs, _patcherState.OBodySettings))
                    {
                        valid = false;
                    }
                }
                if (!valid)
                {
                    _logger.LogMessage(assetPackErrors);
                }
            }

            if (_patcherState.GeneralSettings.BodySelectionMode != BodyShapeSelectionMode.None)
            {
                if (!_miscValidation.VerifyRaceMenuInstalled())
                {
                    valid = false;
                }
                else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen && !_miscValidation.VerifyRaceMenuIniForBodyGen())
                {
                    valid = false;
                }
                else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide && !_miscValidation.VerifyRaceMenuIniForBodySlide())
                {
                    valid = false;
                }

                if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodySlide)
                {
                    if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.OBody)
                    {
                        if (!_miscValidation.VerifyOBodyInstalled())
                        {
                            valid = false;
                        }

                        if (!_miscValidation.VerifyJContainersInstalled(false))
                        {
                            valid = false;
                        }

                        if (_patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && !_miscValidation.VerifyOBodyTemplateJsonExists())
                        {
                            valid = false;
                        }
                    }
                    else if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.AutoBody)
                    {
                        if (!_miscValidation.VerifyAutoBodyInstalled())
                        {
                            valid = false;
                        }

                        if (_patcherState.OBodySettings.AutoBodySelectionMode == AutoBodySelectionMode.JSON && !_miscValidation.VerifyJContainersInstalled(false))
                        {
                            valid = false;
                        }
                    }

                    if (!_miscValidation.VerifyGeneratedTriFilesForOBody(_patcherState.OBodySettings))
                    {
                        valid = false;
                    }

                    if (!_miscValidation.VerifyBodySlideUniqueLabels())
                    {
                        valid = false;
                    }

                    if (!_miscValidation.VerifyReferencedBodySlides())
                    {
                        valid = false;
                    }

                    //if (!MiscValidation.VerifySPIDInstalled(env.DataFolderPath, false))
                    //{
                    //    valid = false;
                    //}
                }
                else if (_patcherState.GeneralSettings.BodySelectionMode == BodyShapeSelectionMode.BodyGen)
                {
                    if (!_miscValidation.VerifyGeneratedTriFilesForBodyGen(_patcherState.AssetPacks, _patcherState.BodyGenConfigs))
                    {
                        valid = false;
                    }
                }
            }

            if (_patcherState.GeneralSettings.bChangeHeight && _patcherState.HeightSettings.bApplyWithoutOverride && !_miscValidation.VerifySkyPatcherInstalled(false))
            {
                valid = false;
            }

            if (_patcherState.GeneralSettings.bChangeHeadParts)
            {
                // Headpart Script mode requires EBD scripts and JContainers
                if (_patcherState.HeadPartSettings.PatchingMode == HeadPartPatchingMode.Script)
                {
                    if (!_miscValidation.VerifyEBDInstalled())
                    {
                        valid = false;
                    }

                    if (!_miscValidation.VerifyJContainersInstalled(false))
                    {
                        valid = false;
                    }
                }
                
                // Headpart SkyPatcher mode requires SkyPatcher
                if (_patcherState.HeadPartSettings.bSkyPatcherModeHeadparts)
                {
                    if (!_miscValidation.VerifySkyPatcherInstalled(false))
                    {
                        valid = false;
                    }
                }
            }
            
            // ══════════════════════════════════════════════════════════════════
            //  Truth Table: Invalid Configuration Detection
            //
            //  The combination of Asset patching mode, Asset SkyPatcher mode,
            //  Headpart patching mode, and Headpart SkyPatcher mode creates
            //  16 possible configurations. Several are invalid and must be
            //  caught before patching begins.
            // ══════════════════════════════════════════════════════════════════

            // Invalid: Headpart Script mode + Headpart SkyPatcher (Cases 2, 6, 10, 14)
            // SkyPatcher surrogate distribution requires Nif mode to bake headparts 
            // into the FaceGen nif; script mode cannot provide this.
            if (_patcherState.GeneralSettings.bChangeHeadParts &&
                _patcherState.HeadPartSettings.PatchingMode == HeadPartPatchingMode.Script &&
                _patcherState.HeadPartSettings.bSkyPatcherModeHeadparts)
            {
                _logger.LogMessage("Invalid configuration: Headpart SkyPatcher mode is incompatible with Headpart Script mode. SkyPatcher headpart distribution requires headparts to be baked into FaceGen nifs (Nif mode).");
                valid = false;
            }

            // Invalid: Asset Nif/No SkyPatcher + Headpart Nif/SkyPatcher (Case 12)
            // Asset Nif mode without SkyPatcher edits the original NPC's FaceGen nif 
            // directly. Headpart Nif + SkyPatcher would use CopyVisualStyle from a 
            // surrogate, which would overwrite those direct edits.
            if (_patcherState.GeneralSettings.bChangeMeshesOrTextures &&
                _patcherState.GeneralSettings.bChangeHeadParts &&
                _patcherState.TexMeshSettings.FacePatchingMode == FacePatchingMode.NifEdit &&
                !_patcherState.TexMeshSettings.bSkyPatcherModeAssets &&
                _patcherState.HeadPartSettings.PatchingMode == HeadPartPatchingMode.NifEdit &&
                _patcherState.HeadPartSettings.bSkyPatcherModeHeadparts)
            {
                _logger.LogMessage("Invalid configuration: Asset Nif mode without SkyPatcher cannot be combined with Headpart Nif + SkyPatcher mode. CopyVisualStyle from the headpart surrogate would overwrite the face texture edits baked directly into the original NPC's FaceGen nif.");
                valid = false;
            }

            // Invalid: Asset Nif/SkyPatcher + Headpart Nif/No SkyPatcher (Case 15)
            // Asset Nif + SkyPatcher uses CopyVisualStyle from a surrogate to transfer
            // baked face textures. Headpart Nif mode without SkyPatcher edits the 
            // original NPC's FaceGen nif directly. The CopyVisualStyle would overwrite
            // those direct headpart edits.
            if (_patcherState.GeneralSettings.bChangeMeshesOrTextures &&
                _patcherState.GeneralSettings.bChangeHeadParts &&
                _patcherState.TexMeshSettings.FacePatchingMode == FacePatchingMode.NifEdit &&
                _patcherState.TexMeshSettings.bSkyPatcherModeAssets &&
                _patcherState.HeadPartSettings.PatchingMode == HeadPartPatchingMode.NifEdit &&
                !_patcherState.HeadPartSettings.bSkyPatcherModeHeadparts)
            {
                _logger.LogMessage("Invalid configuration: Asset Nif + SkyPatcher mode cannot be combined with Headpart Nif mode without SkyPatcher. CopyVisualStyle from the asset surrogate would overwrite the headpart edits baked directly into the original NPC's FaceGen nif.");
                valid = false;
            }

            List<string> itemsWithBlankAttributes = new();
            if (!_miscValidation.VerifyBlankAttributes(itemsWithBlankAttributes))
            {
                _logger.LogMessage("The following items have blank Allowed or Disallowed Attributes, which can prevent proper patcher execution:");
                _logger.LogMessage(itemsWithBlankAttributes);
                valid = false;
            }    

            if (!valid)
            {
                _logger.LogErrorWithStatusUpdate("Could not run the patcher. Please correct the errors above.", ErrorType.Error);
            }

            return valid;
        }
    }
}
