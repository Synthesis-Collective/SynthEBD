using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class JContainersDomain
{
    private readonly PatcherIO _patcherIO;
    private readonly SynthEBDPaths _paths;
    public JContainersDomain(PatcherIO patcherIO, SynthEBDPaths paths)
    {
        _patcherIO = patcherIO;
        _paths = paths;
    }
    public void CreateSynthEBDDomain()
    {
        string domainPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "JCData", "Domains", "PSM_SynthEBD");
        PatcherIO.CreateDirectoryIfNeeded(domainPath, PatcherIO.PathType.Directory);

        string domainScriptPath = Path.Combine(_paths.ResourcesFolderPath, "JContainers Domain", "PSM_SynthEBD.pex");
        string domainScriptDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "PSM_SynthEBD.pex");
        _patcherIO.TryCopyResourceFile(domainScriptPath, domainScriptDestPath);
    }
}
