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
        private readonly VM_SettingsTexMesh _texMeshSettingsVM;
        public PreRunValidation(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, MiscValidation miscValidation, VM_SettingsTexMesh texMeshSettingsVM)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _miscValidation = miscValidation;
            _texMeshSettingsVM = texMeshSettingsVM;
        }

        public bool ValidatePatcherState()
        {
            bool valid = true;

            if (_patcherState.GeneralSettings.bChangeMeshesOrTextures)
            {
                if (!_miscValidation.VerifyEBDInstalled())
                {
                    valid = false;
                }

                if (!_texMeshSettingsVM.ValidateAllConfigs(_patcherState.BodyGenConfigs, _patcherState.OBodySettings, out var configErrors)) // check config files for errors
                {
                    _logger.LogMessage(configErrors);
                    valid = false;
                }

            }

            if (_patcherState.GeneralSettings.BodySelectionMode != BodyShapeSelectionMode.None)
            {
                if (!_miscValidation.VerifyRaceMenuInstalled(_environmentProvider.DataFolderPath))
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
                        if (!_miscValidation.VerifyOBodyInstalled(_environmentProvider.DataFolderPath))
                        {
                            valid = false;
                        }

                        if (!_miscValidation.VerifyJContainersInstalled(_environmentProvider.DataFolderPath, false))
                        {
                            valid = false;
                        }
                    }
                    else if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.AutoBody)
                    {
                        if (!_miscValidation.VerifyAutoBodyInstalled(_environmentProvider.DataFolderPath))
                        {
                            valid = false;
                        }

                        if (_patcherState.OBodySettings.AutoBodySelectionMode == AutoBodySelectionMode.JSON && !_miscValidation.VerifyJContainersInstalled(_environmentProvider.DataFolderPath, false))
                        {
                            valid = false;
                        }
                    }

                    if (!_miscValidation.VerifyGeneratedTriFilesForOBody(_patcherState.OBodySettings))
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

                if (!_miscValidation.VerifyJContainersInstalled(_environmentProvider.DataFolderPath, false))
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                _logger.LogErrorWithStatusUpdate("Could not run the patcher. Please correct the errors above.", ErrorType.Error);
            }

            return valid;
        }
    }
}
