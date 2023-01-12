using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class JContainersDomain
{
    private readonly IEnvironmentStateProvider _stateProvider;
    private readonly PatcherIO _patcherIO;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    public JContainersDomain(IEnvironmentStateProvider stateProvider, PatcherIO patcherIO, SynthEBDPaths paths, Logger logger)
    {
        _stateProvider = stateProvider;
        _patcherIO = patcherIO;
        _paths = paths;
        _logger = logger;
    }
    public void CreateSynthEBDDomain()
    {
        string domainPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "JCData", "Domains", "PSM_SynthEBD");
        PatcherIO.CreateDirectoryIfNeeded(domainPath, PatcherIO.PathType.Directory);

        Task.Run(() =>  PatcherIO.WriteTextFile(Path.Combine(domainPath, "SynthEBD.txt"), "This file exists only to make sure that Vortex deploys the containing folder, without which some of SynthEBD's functionality doesn't work. But since you're here, please consider endorsing ;)", _logger));

        string domainScriptPath = Path.Combine(_stateProvider.InternalDataPath, "JContainers Domain", "PSM_SynthEBD.pex");
        string domainScriptDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "PSM_SynthEBD.pex");
        _patcherIO.TryCopyResourceFile(domainScriptPath, domainScriptDestPath, _logger);
    }
}
