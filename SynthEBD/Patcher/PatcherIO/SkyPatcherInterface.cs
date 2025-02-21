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

    private List<string> outputLines;

    public SkyPatcherInterface(IOutputEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, SynthEBDPaths paths, Logger logger)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _paths = paths;
        _logger = logger;
        
        Reinitialize();
    }

    public void Reinitialize()
    {
        outputLines = new List<string>();
    }

    public void ApplyFace(FormKey applyTo, FormKey faceTemplate)
    {
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string template = BodyGenWriter.FormatFormKeyForBodyGen(faceTemplate);
        
        outputLines.Add($"filterByNPCs={npc}:copyVisualStyle={template}");
    }
    
    public void ApplySkin(FormKey applyTo, FormKey skinFk)
    {
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string skin = BodyGenWriter.FormatFormKeyForBodyGen(skinFk);
        
        outputLines.Add($"filterByNPCs={npc}:skin={skin}");
    }
    
    public void ApplyHeight(FormKey applyTo, float heightFlt)
    {
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(applyTo); 
        string height = heightFlt.ToString();
        
        outputLines.Add($"filterByNPCs={npc}:height={height}");
    }

    public void WriteIni()
    {
        string destinationPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
        PatcherIO.CreateDirectoryIfNeeded(destinationPath, PatcherIO.PathType.File);
        
        Task.Run(() => PatcherIO.WriteTextFile(destinationPath, outputLines, _logger));
    }
}