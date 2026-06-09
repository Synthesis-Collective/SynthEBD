using DynamicData;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Emits the OBody/AutoBody/BodySlide runtime artifacts: the SKSE-based spell, magic effect, and loader
/// quest records that apply BodySlide presets in-game, the supporting Papyrus scripts, and the assignment
/// data (JSON dictionary, autoBody <c>morphs.ini</c>, or OBody native <c>OBody_presetDistributionConfig.json</c>)
/// built from <see cref="Patcher.BodySlideTracker"/>. Runs near the end of the pipeline for body-shape output.
/// </summary>
public class OBodyWriter
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherIO _patcherIO;
    private readonly Converters _converters;
    private HashSet<string> _loadOrderCaseSensitive = new(); // should match capitalization on the drive
    /// <summary>Creates a writer with the environment, patcher state, paths, IO helper, and converters used to emit BodySlide records and data files.</summary>
    public OBodyWriter(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO, Converters converters)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _patcherIO = patcherIO;
        _converters = converters;
    }

    /// <summary>
    /// Creates the constant-effect spell + scripted magic effect (<c>SynthEBDBodySlideScript</c>) that applies a
    /// BodySlide preset to its target NPC. Wires the TargetMod (OBody vs AutoBody) and verbose-mode globals into
    /// the script properties. Side effect: adds new MGEF and SPEL records to <paramref name="outputMod"/>.
    /// </summary>
    /// <param name="gBodySlideVerboseMode">Global toggling verbose in-game logging for the BodySlide script.</param>
    /// <returns>The created BodySlide-application spell.</returns>
    public Spell CreateOBodyAssignmentSpell(ISkyrimMod outputMod, GlobalShort gBodySlideVerboseMode)
    {
        // create MGEF first
        MagicEffect MGEFApplyBodySlide = outputMod.MagicEffects.AddNew();

        // create Spell (needed for MGEF script)
        Spell SPELApplyBodySlide = outputMod.Spells.AddNew();

        MGEFApplyBodySlide.EditorID = "SynthEBDBodySlideMGEF";
        MGEFApplyBodySlide.Name = "Applies BodySlide assignment to NPC";
        MGEFApplyBodySlide.Flags |= MagicEffect.Flag.HideInUI;
        MGEFApplyBodySlide.Flags |= MagicEffect.Flag.NoDeathDispel;
        MGEFApplyBodySlide.Archetype = new MagicEffectArchetype()
        {
            Type = MagicEffectArchetype.TypeEnum.Script
        };
        MGEFApplyBodySlide.TargetType = TargetType.Self;
        MGEFApplyBodySlide.CastType = CastType.ConstantEffect;
        MGEFApplyBodySlide.VirtualMachineAdapter = new VirtualMachineAdapter();

        ScriptEntry ScriptApplyBodySlide = new ScriptEntry() { Name = "SynthEBDBodySlideScript"};

        ScriptStringProperty targetModProperty = new ScriptStringProperty() { Name = "TargetMod", Flags = ScriptProperty.Flag.Edited };
        switch(_patcherState.GeneralSettings.BSSelectionMode)
        {
            case BodySlideSelectionMode.OBody: targetModProperty.Data = "OBody"; break;
            case BodySlideSelectionMode.AutoBody: targetModProperty.Data = "AutoBody"; break;
        }
        ScriptApplyBodySlide.Properties.Add(targetModProperty);

        ScriptObjectProperty verboseModeProperty = new ScriptObjectProperty() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited } ;
        verboseModeProperty.Object.SetTo(gBodySlideVerboseMode);
        ScriptApplyBodySlide.Properties.Add(verboseModeProperty);

        MGEFApplyBodySlide.VirtualMachineAdapter.Scripts.Add(ScriptApplyBodySlide);

        // Edit Spell
        SPELApplyBodySlide.EditorID = "SynthEBDBodySlideSPEL";
        SPELApplyBodySlide.Name = "Applies BodySlide assignment to NPC";
        SPELApplyBodySlide.CastType = CastType.ConstantEffect;
        SPELApplyBodySlide.TargetType = TargetType.Self;
        SPELApplyBodySlide.Type = SpellType.Ability;
        SPELApplyBodySlide.EquipmentType.SetTo(Skyrim.EquipType.EitherHand);
            
        Effect BodySlideShellEffect = new Effect();
        BodySlideShellEffect.BaseEffect.SetTo(MGEFApplyBodySlide);
        BodySlideShellEffect.Data = new EffectData();
        SPELApplyBodySlide.Effects.Add(BodySlideShellEffect);

        return SPELApplyBodySlide;
    }

    /// <summary>
    /// Creates the start-game-enabled, run-once loader quest (forced-referenced to the player) whose player-alias
    /// script bootstraps the BodySlide system at game start. Side effects: adds a new Quest record to
    /// <paramref name="outputMod"/> and copies the loader player-alias .pex script into the output Scripts folder.
    /// </summary>
    /// <param name="gEnableBodySlideScript">Global gating whether the BodySlide loader script runs.</param>
    /// <param name="gBodySlideVerboseMode">Global toggling verbose in-game logging.</param>
    public void CreateBodySlideLoaderQuest(ISkyrimMod outputMod, GlobalShort gEnableBodySlideScript, GlobalShort gBodySlideVerboseMode)
    {
        Quest bsLoaderQuest = outputMod.Quests.AddNew();
        bsLoaderQuest.Name = "Loads SynthEBD BodySlide Assignments";
        bsLoaderQuest.EditorID = "SynthEBDBSLoaderQuest";

        bsLoaderQuest.Flags |= Quest.Flag.StartGameEnabled;
        bsLoaderQuest.Flags |= Quest.Flag.RunOnce;

        QuestAlias playerQuestAlias = new QuestAlias();
        FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
        playerQuestAlias.ForcedReference.SetTo(playerRefFK);
        bsLoaderQuest.Aliases.Add(playerQuestAlias);

        QuestAdapter bsLoaderScriptAdapter = new QuestAdapter();

        /*
        ScriptEntry bsLoaderScriptEntry = new ScriptEntry() { Name = "SynthEBDBodySlideLoaderQuestScript", Flags = ScriptEntry.Flag.Local };
        ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "SynthEBDDataBaseLoaded", Flags = ScriptProperty.Flag.Edited };
        settingsLoadedProperty.Object.SetTo(settingsLoadedGlobal.FormKey);
        bsLoaderScriptEntry.Properties.Add(settingsLoadedProperty);
        bsLoaderScriptAdapter.Scripts.Add(bsLoaderScriptEntry);
        */

        QuestFragmentAlias loaderQuestFragmentAlias = new QuestFragmentAlias();
        loaderQuestFragmentAlias.Property = new ScriptObjectProperty() { Name = "000 Player" };
        loaderQuestFragmentAlias.Property.Object.SetTo(bsLoaderQuest.FormKey);
        loaderQuestFragmentAlias.Property.Name = "Player";
        loaderQuestFragmentAlias.Property.Alias = 0;

        ScriptEntry playerAliasScriptEntry = new ScriptEntry();
        playerAliasScriptEntry.Name = "SynthEBDBodySlideLoaderPAScript";
        playerAliasScriptEntry.Flags = ScriptEntry.Flag.Local;

        ScriptObjectProperty loaderQuestActiveProperty = new ScriptObjectProperty() { Name = "BodySlideScriptActive", Flags = ScriptProperty.Flag.Edited };
        loaderQuestActiveProperty.Object.SetTo(gEnableBodySlideScript);
        playerAliasScriptEntry.Properties.Add(loaderQuestActiveProperty);

        ScriptObjectProperty verboseModeProperty = new ScriptObjectProperty() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited };
        verboseModeProperty.Object.SetTo(gBodySlideVerboseMode);
        playerAliasScriptEntry.Properties.Add(verboseModeProperty);

        loaderQuestFragmentAlias.Scripts.Add(playerAliasScriptEntry);
        bsLoaderScriptAdapter.Aliases.Add(loaderQuestFragmentAlias);
        bsLoaderQuest.VirtualMachineAdapter = bsLoaderScriptAdapter;

        // copy quest alias script
        string questAliasSourcePath = Path.Combine(_environmentProvider.InternalDataPath, "BodySlideScripts", "SynthEBDBodySlideLoaderPAScript.pex");
        string questAliasDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideLoaderPAScript.pex");
        _patcherIO.TryCopyResourceFile(questAliasSourcePath, questAliasDestPath, _logger);
    }

    /// <summary>Copies the main <c>SynthEBDBodySlideScript.pex</c> from internal data into the output Scripts folder.</summary>
    public void CopyBodySlideScript()
    {
        string sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "BodySlideScripts", "SynthEBDBodySlideScript.pex");
        string destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideScript.pex");
        _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
    }

    /*
    public static void WriteBodySlideSPIDIni(Spell bodySlideSpell, Settings_OBody obodySettings, SkyrimMod outputMod)
    {
        string str = "Spell = " + bodySlideSpell.FormKey.ToString().Replace(":", " - ") + " | ActorTypeNPC | NONE | NONE | "; // original format - SPID auto-updates but this is compatible with old SPID versions

        bool hasMaleBodySlides = obodySettings.BodySlidesMale.Where(x => obodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any();
        bool hasFemaleBodySlides = obodySettings.BodySlidesFemale.Where(x => obodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any();

        if (!hasMaleBodySlides && !hasFemaleBodySlides) { return; }
        else if (hasMaleBodySlides && !hasFemaleBodySlides) { str += "M"; }
        else if (!hasMaleBodySlides && hasFemaleBodySlides) { str += "F"; }

        string outputPath = Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini");
        Task.Run(() => PatcherIO.WriteTextFile(outputPath, str));
    }
    */

    /// <summary>
    /// Writes the script-mode BodySlide assignments as <c>SynthEBD/BodySlideAssignments.json</c> (a JContainers-keyed
    /// FormKey to preset-name map). No-ops with a log message when no BodySlides were assigned. Side effect: writes the
    /// JSON file. Throws if called while OBody is in Native selection mode (which uses a different output path).
    /// </summary>
    public void WriteAssignmentDictionaryScriptMode()
    {
        if (_patcherState.GeneralSettings.BSSelectionMode == BodySlideSelectionMode.OBody && _patcherState.OBodySettings.OBodySelectionMode == OBodySelectionMode.Native)
        {
            throw new Exception("WriteAssignmentDictionaryScriptMode() cannot be called when OBodySettings.OBodySelectionMode is Native");
        }

        if (Patcher.BodySlideTracker.Count == 0)
        {
            _logger.LogMessage("No BodySlides were assigned to any NPCs. OBody database will not be generated.");
            return;
        }

        var outputDictionary = new Dictionary<string, string>();
        foreach (var entry in Patcher.BodySlideTracker)
        {
            if (!entry.Value.Any()) { continue; } // guard against an empty preset list, matching WriteAssignmentIni; First() would otherwise throw
            outputDictionary.TryAdd(entry.Key.ToJContainersCompatiblityKey(), entry.Value.First());
        }
        string outputStr = JSONhandler<Dictionary<string, string>>.Serialize(outputDictionary, out bool success, out string exception);

        var destPath = Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideAssignments.json");

        try
        {
            _logger.LogMessage("Writing BodySlide Assignments to " + destPath);
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.WriteAllText(destPath, outputStr);
        }
        catch
        {
            _logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
        }
    }

    /// <summary>
    /// Writes the AutoBody BodySlide assignments as <c>autoBody/Config/morphs.ini</c> ("FormKey=preset" lines, first
    /// preset per NPC). No-ops with a log message when nothing was assigned. Side effect: writes the ini file.
    /// </summary>
    public void WriteAssignmentIni()
    {
        if (Patcher.BodySlideTracker.Count == 0)
        {
            _logger.LogMessage("No BodySlides were assigned to any NPCs");
            return;
        }

        string outputStr = "";

        foreach (var entry in Patcher.BodySlideTracker)
        {
            if (!entry.Value.Any())
            {
                continue;
            }
            outputStr += BodyGenWriter.FormatFormKeyForBodyGen(entry.Key) + "=" + entry.Value.First() + Environment.NewLine;
        }

        var destPath = Path.Combine(_paths.OutputDataFolder, "autoBody", "Config", "morphs.ini");

        try
        {
            _logger.LogMessage("Writing BodySlide Assignments to " + destPath);
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.WriteAllText(destPath, outputStr);
        }
        catch
        {
            _logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
        }
    }

    /// <summary>Deletes stale ini-mode outputs (autoBody and BodyGenData <c>morphs.ini</c>) when running in JSON/script mode. Side effect: deletes files.</summary>
    public void ClearOutputForJsonMode()
    {
        HashSet<string> toClear = new HashSet<string>()
        {
            Path.Combine(_paths.OutputDataFolder, "autoBody", "Config", "morphs.ini"),
            Path.Combine(_paths.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", _patcherState.GeneralSettings.PatchFileName, "morphs.ini")
        };

        foreach (string path in toClear)
        {
            if (File.Exists(path))
            {
                _patcherIO.TryDeleteFile(path, _logger);
            }
        }
    }

    /// <summary>Deletes stale JSON/script-mode outputs (BodyGenData <c>morphs.ini</c> and <c>BodySlideAssignments.json</c>) when running in ini mode. Side effect: deletes files.</summary>
    public void ClearOutputForIniMode()
    {
        HashSet<string> toClear = new HashSet<string>()
        {
            //Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"),
            //Path.Combine(_paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"),
            Path.Combine(_paths.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", _patcherState.GeneralSettings.PatchFileName, "morphs.ini"),
            Path.Combine(_paths.OutputDataFolder, "SynthEBD", "BodySlideAssignments.json")
        };

        foreach (string path in toClear)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    _logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                }
            }
        }
    }

    /// <summary>
    /// Writes assignments in OBody Native format by loading the existing <c>OBody_presetDistributionConfig.json</c>
    /// template from the data folder, clearing and repopulating its <c>npcFormID</c> section (grouped by plugin,
    /// keyed by FormID) from <see cref="Patcher.BodySlideTracker"/>, and saving it to the output. No-ops with a log
    /// message when nothing was assigned. Side effects: reads the template, writes the output JSON; logs and returns
    /// on load/parse failure or unresolvable FormKeys.
    /// </summary>
    public void WriteNativeAssignmentDictionary()
    {
        if (Patcher.BodySlideTracker.Count == 0)
        {
            _logger.LogMessage("No BodySlides were assigned to any NPCs");
            return;
        }

        var templatePath = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "OBody_presetDistributionConfig.json");
        var templateJson = JSONhandler<dynamic>.LoadJSONFile(templatePath, out bool success, out string exceptionStr);

        if (!success)
        {
            _logger.LogError("Could not open OBody_presetDistributionConfig.json for editing. The following error was encountered: " + Environment.NewLine + exceptionStr);
            return;
        }

        Dictionary<string, Dictionary<string, List<string>>> npcFormIDAssignments = new();
        try
        {
            npcFormIDAssignments = templateJson.npcFormID.ToObject<Dictionary<string, Dictionary<string, List<string>>>>();
        }
        catch
        {
            _logger.LogError("Error parsing OBody_presetDistributionConfig.json");
            return;
        }

        npcFormIDAssignments.Clear();

        var npcsGroupedByModKey = Patcher.BodySlideTracker.GroupBy(x => x.Key.ModKey).ToArray();

        foreach (var modGroup in npcsGroupedByModKey)
        {
            Dictionary<string, List<string>> modEntry = new();

            npcFormIDAssignments.Add(modGroup.Key.ToString(), modEntry);

            foreach (var entry in modGroup)
            {
                if (_converters.TryFormKeyStringToFormIDString(entry.Key.ToString(), out string formID))
                {
                    modEntry.Add(formID, new List<string>() { entry.Value });
                }
                else
                {
                    _logger.LogError("Cannot obtain FormID for FormKey " + entry.Key.ToString());
                }
            }
        }

        templateJson.npcFormID = JObject.FromObject(npcFormIDAssignments);


        string outputStr = JSONhandler<dynamic>.Serialize(templateJson, out success, out string exception);

        var destPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "OBody_presetDistributionConfig.json");

        try
        {
            _logger.LogMessage("Writing BodySlide Assignments to " + destPath);
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.WriteAllText(destPath, outputStr);
        }
        catch
        {
            _logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
        }
    }
}