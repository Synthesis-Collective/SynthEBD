using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class FirstLaunch
    {
        private readonly IStateProvider _stateProvider;
        private readonly SynthEBDPaths _paths;
        public FirstLaunch(IStateProvider stateProvider, SynthEBDPaths paths)
        {
            _stateProvider = stateProvider;
            _paths = paths;
        }

        public void OnFirstLaunch()
        {
            string defaultHeightConfigStartPath = Path.Combine(_stateProvider.InternalDataPath, "FirstLaunchResources", "Default Config.json");
            string defaultHeightConfigDestPath = Path.Combine(_paths.HeightConfigDirPath, "Default Config.json");
            if (!File.Exists(defaultHeightConfigDestPath))
            {
                PatcherIO.CreateDirectoryIfNeeded(defaultHeightConfigDestPath, PatcherIO.PathType.File);
                File.Copy(defaultHeightConfigStartPath, defaultHeightConfigDestPath, false);
            }

            string defaultRecordTemplatesStartPath = Path.Combine(_stateProvider.InternalDataPath, "FirstLaunchResources", "Record Templates.esp");
            string defaultRecordTemplatesDestPath = Path.Combine(_paths.RecordTemplatesDirPath, "Record Templates.esp");

            if (!File.Exists(defaultRecordTemplatesDestPath))
            {
                PatcherIO.CreateDirectoryIfNeeded(defaultRecordTemplatesDestPath, PatcherIO.PathType.File);
                File.Copy(defaultRecordTemplatesStartPath, defaultRecordTemplatesDestPath, false);
            }
        }

    }
}
