using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class UpdateHandler // handles backward compatibility for previous SynthEBD versions
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherState _patcherState;
    private readonly PatcherIO _patcherIO;
    private readonly Logger _logger;
    private readonly VM_Settings_General _generalVM;
    private readonly VM_SettingsTexMesh _texMeshVM;
    private readonly VM_RaceGrouping.Factory _raceGroupingFactory;

    public UpdateHandler(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, PatcherState patcherState, PatcherIO patcherIO, Logger logger, VM_Settings_General generalVM, VM_SettingsTexMesh texMeshVM, VM_RaceGrouping.Factory raceGroupingFactory)
    {
        _environmentProvider = environmentProvider;
        _paths = paths;
        _patcherState = patcherState;
        _patcherIO = patcherIO; 
        _logger = logger;
        _generalVM = generalVM;
        _texMeshVM = texMeshVM;
        _raceGroupingFactory = raceGroupingFactory;
    }

    public void CheckBackwardCompatibility()
    {
        UpdateAssetPacks(_texMeshVM);
        UpdateV1012(_generalVM);
        UpdateV1013(_generalVM);
        UpdateV1013RecordTemplates();
    }
    private void UpdateAssetPacks(VM_SettingsTexMesh texMeshVM)
    {
        texMeshVM.ConfigUpdateAll(new());
    }    
    public void CleanSPIDiniHeadParts()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini"), _logger);
    }
    public void CleanSPIDiniOBody()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"), _logger);
    }
    public void CleanOldBodySlideDict()
    {
        _patcherIO.TryDeleteFile(Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"), _logger);
    }

    private void UpdateV1012(VM_Settings_General generalVM)
    {
        if (!_patcherState.UpdateLog.Performed1_0_1_2Update)
        {
            var missingNames = v1012UniqueNameExclusions.Where(x => !generalVM.LinkedNameExclusions.Select(y => y.Content).Contains(x)).ToHashSet();
            var dispText = "v1.0.1.2 Update: It is suggested to add the following names to your Linked Unique NPC Name Exclusions. Would you like to do this automatically?" + Environment.NewLine + String.Join(Environment.NewLine, missingNames);
            if (missingNames.Any() && CustomMessageBox.DisplayNotificationYesNo("Update Unique Name Exclusions?", dispText))
            {
                foreach (var name in missingNames)
                {
                    generalVM.LinkedNameExclusions.Add(new(name, generalVM.LinkedNameExclusions));
                }
            }

            _patcherState.UpdateLog.Performed1_0_1_2Update = true;
        }
    }

    private void UpdateV1013(VM_Settings_General generalVM)
    {
        if (!_patcherState.UpdateLog.Performed1_0_1_3Update)
        {
            if (!generalVM.RaceGroupingEditor.RaceGroupings.Where(x => x.Label == DefaultRaceGroupings.HumanoidPlayableNonVampire.Label).Any())
            {
                var newGrouping = _raceGroupingFactory(DefaultRaceGroupings.HumanoidPlayableNonVampire, generalVM.RaceGroupingEditor);
                generalVM.RaceGroupingEditor.RaceGroupings.Add(newGrouping);
            }
            _patcherState.UpdateLog.Performed1_0_1_3Update = true;
        }
    }

    private void UpdateV1013RecordTemplates()
    {
        if (_patcherState.UpdateLog.Performed1_0_1_3RTUpdate)
        {
            return;
        }
        string defaultRecordTemplatesStartPath = Path.Combine(_environmentProvider.InternalDataPath, "FirstLaunchResources");

        var newTemplateNames = new string[] { "Record Templates - 3BA - pamonha.esp", "Record Templates - BHUNP - pamonha.esp" };

        foreach (var newPlugin in newTemplateNames)
        {
            var source = Path.Combine(defaultRecordTemplatesStartPath, newPlugin);
            var dest = Path.Combine(_paths.RecordTemplatesDirPath, newPlugin);

            if (File.Exists(source) && !File.Exists(dest))
            {
                if (!_patcherIO.TryCopyResourceFile(source, dest, _logger, out string errorStr))
                {
                    _logger.LogError("Failed to copy new record template during update." + Environment.NewLine + "Source: " + source + Environment.NewLine + "Destination: " + dest + Environment.NewLine + errorStr);
                }
            }
        }
    }

    public Dictionary<string, string> V09PathReplacements { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Diffuse", "Diffuse.RawPath" },
        { "NormalOrGloss", "NormalOrGloss.RawPath" },
        { "GlowOrDetailMap", "GlowOrDetailMap.RawPath" },
        { "BacklightMaskOrSpecular", "BacklightMaskOrSpecular.RawPath" },
        { "Height", "Height.RawPath" }
    };

    public HashSet<string> v1012UniqueNameExclusions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Courier",
        "The Guardian",
        "Imperial Champion",
        "Stormcloak Champion",
        "Redoran Guard",
        "Reclamation Priest",
        "Imperial Soldier",
        "Enthralled Wizard",
        "Nord",
        "Torture Victim"
    };
}

public class UpdateLog
{
    public bool Performed1_0_1_2Update { get; set; } = false;
    public bool Performed1_0_1_3Update { get; set; } = false;
    public bool Performed1_0_1_3RTUpdate { get; set; } = false;
}
