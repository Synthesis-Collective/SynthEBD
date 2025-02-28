using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;

namespace SynthEBD
{
    public class PreRunValidation
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly MiscValidation _miscValidation;
        private readonly AssetPackValidator _assetPackValidator;
        public PreRunValidation(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, MiscValidation miscValidation, AssetPackValidator assetPackValidator)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _miscValidation = miscValidation;
            _assetPackValidator = assetPackValidator;
        }

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
                
                if (_patcherState.TexMeshSettings.bPureScriptMode && !_miscValidation.VerifySkyPatcherInstalled(false))
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

                        if (_patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native && !_miscValidation.VerifyOBodyTemplateJsonExits())
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
                if (!_miscValidation.VerifyEBDInstalled())
                {
                    valid = false;
                }

                //if (!MiscValidation.VerifySPIDInstalled(env.DataFolderPath, false))
                //{
                //    valid = false;
                //}

                if (!_miscValidation.VerifyJContainersInstalled(false))
                {
                    valid = false;
                }
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
