using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Sets up SynthEBD's JContainers domain in the output folder: creates the <c>PSM_SynthEBD</c> domain
/// directory (with a placeholder file so Vortex deploys it) and copies the domain Papyrus script.
/// </summary>
public class JContainersDomain
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherIO _patcherIO;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    /// <summary>Initializes a new <see cref="JContainersDomain"/> with the environment, IO helper, paths, and logger.</summary>
    public JContainersDomain(IEnvironmentStateProvider environmentProvider, PatcherIO patcherIO, SynthEBDPaths paths, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _patcherIO = patcherIO;
        _paths = paths;
        _logger = logger;
    }
    /// <summary>
    /// Creates the JContainers domain folder under the output <c>SKSE/Plugins/JCData/Domains</c> path, writes a
    /// placeholder text file into it (to force Vortex deployment), and copies the domain <c>.pex</c> script to
    /// the output <c>Scripts</c> folder.
    /// </summary>
    public void CreateSynthEBDDomain()
    {
        string domainPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "JCData", "Domains", "PSM_SynthEBD");
        PatcherIO.CreateDirectoryIfNeeded(domainPath, PatcherIO.PathType.Directory);

        Task.Run(() =>  PatcherIO.WriteTextFile(Path.Combine(domainPath, "SynthEBD.txt"), "This file exists only to make sure that Vortex deploys the containing folder, without which some of SynthEBD's functionality doesn't work. But since you're here, please consider endorsing ;)", _logger));

        string domainScriptPath = Path.Combine(_environmentProvider.InternalDataPath, "JContainers Domain", "PSM_SynthEBD.pex");
        string domainScriptDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "PSM_SynthEBD.pex");
        _patcherIO.TryCopyResourceFile(domainScriptPath, domainScriptDestPath, _logger);
    }
}
