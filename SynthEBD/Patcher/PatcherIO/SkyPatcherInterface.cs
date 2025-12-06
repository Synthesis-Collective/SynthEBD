using Microsoft.IO;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Converters;

namespace SynthEBD;

public class SkyPatcherInterface
{
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    private readonly PatcherIO _patcherIO;

    private List<string> _outputLines;

    public SkyPatcherInterface(IOutputEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, SynthEBDPaths paths, Logger logger, PatcherIO patcherIO)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _paths = paths;
        _logger = logger;
        _patcherIO = patcherIO;
        
        Reinitialize();
    }

    public void Reinitialize()
    {
        _outputLines = new List<string>();
        ClearIni();
    }

    public void ApplyFace(FormKey applyTo, FormKey faceTemplate) // This doesn't work if the face texture isn't baked into the facegen nif. Not useful for SynthEBD.
    {
        if (applyTo.IsNull || faceTemplate.IsNull)
        {
            return;
        }
        
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string template = BodyGenWriter.FormatFormKeyForBodyGen(faceTemplate);
        
        _outputLines.Add($"filterByNPCs={npc}:copyVisualStyle={template}");
    }
    
    public void ApplySkin(FormKey applyTo, FormKey skinFk)
    {
        if (applyTo.IsNull || skinFk.IsNull)
        {
            return;
        }
        
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string skin = BodyGenWriter.FormatFormKeyForBodyGen(skinFk);
        
        _outputLines.Add($"filterByNPCs={npc}:skin={skin}");
    }
    
    public void ApplyHeight(FormKey applyTo, float heightFlt)
    {
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string height = heightFlt.ToString();
        
        _outputLines.Add($"filterByNPCs={npc}:height={height}");
    }

    public void WriteIni()
    {
        string destinationPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
        PatcherIO.CreateDirectoryIfNeeded(destinationPath, PatcherIO.PathType.File);
        
        Task.Run(() => PatcherIO.WriteTextFile(destinationPath, _outputLines, _logger));
    }

    private void ClearIni()
    {
        string destinationPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
        _patcherIO.TryDeleteFile(destinationPath, _logger);
    }

    public bool HasEntries()
    {
        return _outputLines.Any();
    }
}