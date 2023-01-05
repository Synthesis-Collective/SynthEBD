using DynamicData;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;

namespace SynthEBD
{
    public class FirstLaunch
    {
        private readonly IStateProvider _stateProvider;
        private readonly SynthEBDPaths _paths;
        private readonly Logger _logger;
        private readonly VM_SettingsHeight _heightSettingsVM;
        private readonly VM_HeightConfig.Factory _heightConfigFactory;
        private readonly VM_HeightAssignment.Factory _heightAssignmentFactory;
        private readonly SettingsIO_AssetPack _assetIO;
        private readonly MainState _mainState;
        public FirstLaunch(IStateProvider stateProvider, SynthEBDPaths paths, Logger logger, VM_SettingsHeight heightSettingsVM, SettingsIO_AssetPack assetIO, MainState mainState, VM_HeightConfig.Factory heightConfigFactory, VM_HeightAssignment.Factory heightAssignmentFactory)
        {
            _stateProvider = stateProvider;
            _paths = paths;
            _logger = logger;
            _heightSettingsVM = heightSettingsVM;
            _assetIO = assetIO;
            _mainState = mainState;
            _heightConfigFactory = heightConfigFactory;
            _heightAssignmentFactory = heightAssignmentFactory;
        }

        public void OnFirstLaunch()
        {
            ShowFirstRunMessage();
            string defaultHeightConfigStartPath = Path.Combine(_stateProvider.InternalDataPath, "FirstLaunchResources", "Default Config.json");
            string defaultHeightConfigDestPath = Path.Combine(_paths.HeightConfigDirPath, "Default Config.json");
            if (!File.Exists(defaultHeightConfigDestPath))
            {
                PatcherIO.CreateDirectoryIfNeeded(defaultHeightConfigDestPath, PatcherIO.PathType.File);
                File.Copy(defaultHeightConfigStartPath, defaultHeightConfigDestPath, false);
                var newConfig = JSONhandler<HeightConfig>.LoadJSONFile(defaultHeightConfigDestPath, out bool heightConfigLoaded, out string heightError);
                if (heightConfigLoaded)
                {
                    
                    VM_HeightConfig.GetViewModelsFromModels(_heightSettingsVM.AvailableHeightConfigs, new List<HeightConfig>() { newConfig }, _heightConfigFactory, _heightAssignmentFactory);
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Could not load default height config. Error: " + Environment.NewLine + heightError, ErrorType.Warning);
                }
            }

            string defaultRecordTemplatesStartPath = Path.Combine(_stateProvider.InternalDataPath, "FirstLaunchResources", "Record Templates.esp");
            string defaultRecordTemplatesDestPath = Path.Combine(_paths.RecordTemplatesDirPath, "Record Templates.esp");

            if (!File.Exists(defaultRecordTemplatesDestPath))
            {
                PatcherIO.CreateDirectoryIfNeeded(defaultRecordTemplatesDestPath, PatcherIO.PathType.File);
                File.Copy(defaultRecordTemplatesStartPath, defaultRecordTemplatesDestPath, false);
                var recordTemplates = _assetIO.LoadRecordTemplates(out bool loadSuccess);
                if (loadSuccess)
                {
                    _mainState.RecordTemplatePlugins = recordTemplates;
                    _mainState.RecordTemplateLinkCache = _mainState.RecordTemplatePlugins.ToImmutableLinkCache();
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Could not load default record templates.", ErrorType.Warning);
                }
            }
        }

        private void ShowFirstRunMessage()
        {
            string message = @"Welcome to SynthEBD
If you are using a mod manager, start by going to the Mod Manager Integration menu and setting up your paths.
If you don't want your patcher output going straight to your Data or Overwrite folder, set your desired Output Path in the General Settings menu.";

            CustomMessageBox.DisplayNotificationOK("", message);
        }
    }
}
