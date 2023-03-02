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
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly SynthEBDPaths _paths;
        private readonly Logger _logger;
        private readonly VM_SettingsHeight _heightSettingsVM;
        private readonly VM_HeightConfig.Factory _heightConfigFactory;
        private readonly VM_HeightAssignment.Factory _heightAssignmentFactory;
        private readonly VM_SettingsModManager _modManager;
        private readonly SettingsIO_AssetPack _assetIO;
        private readonly PatcherState _patcherState;
        private readonly CustomMessageBox _customMessageBox;
        public FirstLaunch(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, Logger logger, VM_SettingsHeight heightSettingsVM, VM_SettingsModManager modManager, SettingsIO_AssetPack assetIO, PatcherState patcherState, VM_HeightConfig.Factory heightConfigFactory, VM_HeightAssignment.Factory heightAssignmentFactory, CustomMessageBox customMessageBox)
        {
            _environmentProvider = environmentProvider;
            _paths = paths;
            _logger = logger;
            _heightSettingsVM = heightSettingsVM;
            _modManager = modManager;
            _assetIO = assetIO;
            _patcherState = patcherState;
            _heightConfigFactory = heightConfigFactory;
            _heightAssignmentFactory = heightAssignmentFactory;
            _customMessageBox = customMessageBox;
        }

        public void OnFirstLaunch()
        {
            ShowFirstRunMessage();
            string defaultHeightConfigStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources", "Default Config.json");
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
            
            if (_heightSettingsVM.SelectedHeightConfig == null || (_heightSettingsVM.SelectedHeightConfig.Label == VM_HeightConfig.DefaultLabel && !_heightSettingsVM.SelectedHeightConfig.HeightAssignments.Any()))
            {
                _heightSettingsVM.SelectedHeightConfig = _heightSettingsVM.AvailableHeightConfigs.FirstOrDefault();
            }

            string defaultRecordTemplatesStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources", "Record Templates.esp");
            string defaultRecordTemplatesDestPath = Path.Combine(_paths.RecordTemplatesDirPath, "Record Templates.esp");

            if (!File.Exists(defaultRecordTemplatesDestPath))
            {
                PatcherIO.CreateDirectoryIfNeeded(defaultRecordTemplatesDestPath, PatcherIO.PathType.File);
                File.Copy(defaultRecordTemplatesStartPath, defaultRecordTemplatesDestPath, false);
                var recordTemplates = _assetIO.LoadRecordTemplates(out bool loadSuccess);
                if (loadSuccess)
                {
                    _patcherState.RecordTemplatePlugins = recordTemplates;
                    _patcherState.RecordTemplateLinkCache = _patcherState.RecordTemplatePlugins.ToImmutableLinkCache();
                }
                else
                {
                    _logger.LogErrorWithStatusUpdate("Could not load default record templates.", ErrorType.Warning);
                }
            }

            if (_environmentProvider.RunMode == EnvironmentMode.Synthesis)
            {
                _modManager.TempFolder = Path.Combine(_environmentProvider.InternalDataPath, "Temp");
            }
        }

        private void ShowFirstRunMessage()
        {
            string message = @"Welcome to SynthEBD
If you are using a mod manager, start by going to the Mod Manager Integration menu and setting up your paths.
If you don't want your patcher output going straight to your Data or Overwrite folder, set your desired Output Data Folder in the General Settings menu.";

            _customMessageBox.DisplayNotificationOK_WindowSafe("", message);
        }
    }
}
