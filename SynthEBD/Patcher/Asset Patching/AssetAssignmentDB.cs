using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class AssetAssignmentDB
{
    private readonly Converters _converters;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherState _patcherState;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherIO _patcherIO;
    
    private Dictionary<string, string> _faceTextureAssignments = new();
    private Dictionary<string, string> _skinTextureAssignments = new();

    public AssetAssignmentDB(Converters converters, Logger logger, SynthEBDPaths paths, PatcherState patcherState, IEnvironmentStateProvider environmentProvider, PatcherIO patcherIO)
    {
        _converters = converters;
        _logger = logger;           
        _paths = paths;
        _patcherState = patcherState;
        _environmentProvider = environmentProvider;
        _patcherIO = patcherIO;
    }

    public void Reinitialize()
    {
        _faceTextureAssignments.Clear();
        _skinTextureAssignments.Clear();
    }

    public void LogNPCAssignments(NPCInfo npcInfo, ISkyrimMod outputMod)
    {
        if (npcInfo.NPC is null)
        {
            return;
        }

        if (npcInfo.NPC.HeadTexture is not null && !npcInfo.NPC.HeadTexture.IsNull &&
            npcInfo.NPC.HeadTexture.FormKey.ModKey.Equals(outputMod.ModKey))
        {
            _faceTextureAssignments.Add(npcInfo.OriginalNPC.FormKey.ToJContainersCompatiblityKey(), npcInfo.NPC.HeadTexture.FormKey.ToString());
        }
        
        if (npcInfo.NPC.WornArmor is not null && !npcInfo.NPC.WornArmor.IsNull &&
            npcInfo.NPC.WornArmor.FormKey.ModKey.Equals(outputMod.ModKey))
        {
            _skinTextureAssignments.Add(npcInfo.OriginalNPC.FormKey.ToJContainersCompatiblityKey(), npcInfo.NPC.WornArmor.FormKey.ToString());
        }
    }
    
    public void WriteAssignmentDictionaryScriptMode()
    {
        if (!_patcherState.TexMeshSettings.bPureScriptMode)
        {
            return;
        }
        
        if (!_faceTextureAssignments.Any() && !_skinTextureAssignments.Any())
        {
            _logger.LogMessage("No assets were assigned to any NPCs. Asset Database will not be generated.");
            return;
        }

        string destPath = string.Empty;
        if (_faceTextureAssignments.Any())
        {
            string outputStr = JSONhandler<Dictionary<string, string>>.Serialize((_faceTextureAssignments), out bool success, out string exception);
            if (!success)
            {
                MessageWindow.DisplayNotificationOK("Failed to generate Face Texture Dictionary", exception);
            }
            else
            {
                try
                {
                    destPath = Path.Combine(_paths.OutputDataFolder, "SynthEBD", "FaceTextureAssignments.json");
                    _logger.LogMessage("Writing Face Texture Assignments to " + destPath);
                    PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
                    File.WriteAllText(destPath, outputStr);
                }
                catch
                {
                    _logger.LogErrorWithStatusUpdate("Could not write Face Texture assignments to " + destPath, ErrorType.Error);
                }
            }
        }
        
        if (_skinTextureAssignments.Any())
        {
            string outputStr = JSONhandler<Dictionary<string, string>>.Serialize((_skinTextureAssignments), out bool success, out string exception);
            if (!success)
            {
                MessageWindow.DisplayNotificationOK("Failed to generate Skin Texture Dictionary", exception);
            }
            else
            {
                try
                {
                    destPath = Path.Combine(_paths.OutputDataFolder, "SynthEBD", "SkinTextureAssignments.json");
                    _logger.LogMessage("Writing Skin Texture Assignments to " + destPath);
                    PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
                    File.WriteAllText(destPath, outputStr);
                }
                catch
                {
                    _logger.LogErrorWithStatusUpdate("Could not write Skin Texture assignments to " + destPath, ErrorType.Error);
                }
            }
        }
    }
    
    public void CreateTextureLoaderQuest(ISkyrimMod outputMod, GlobalShort gEnableTextureLoaderScript, GlobalShort gTextureLoaderVerboseMode)
    {
        Quest texLoaderQuest = outputMod.Quests.AddNew();
        texLoaderQuest.Name = "Loads SynthEBD BodySlide Assignments";
        texLoaderQuest.EditorID = "SynthEBDtexLoaderQuest";

        texLoaderQuest.Flags |= Quest.Flag.StartGameEnabled;
        texLoaderQuest.Flags |= Quest.Flag.RunOnce;

        QuestAlias playerQuestAlias = new QuestAlias();
        FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
        playerQuestAlias.ForcedReference.SetTo(playerRefFK);
        texLoaderQuest.Aliases.Add(playerQuestAlias);

        QuestAdapter texLoaderScriptAdapter = new QuestAdapter();

        QuestFragmentAlias loaderQuestFragmentAlias = new QuestFragmentAlias();
        loaderQuestFragmentAlias.Property = new ScriptObjectProperty() { Name = "000 Player" };
        loaderQuestFragmentAlias.Property.Object.SetTo(texLoaderQuest.FormKey);
        loaderQuestFragmentAlias.Property.Name = "Player";
        loaderQuestFragmentAlias.Property.Alias = 0;

        ScriptEntry playerAliasScriptEntry = new ScriptEntry();
        playerAliasScriptEntry.Name = "SynthEBDTextureLoaderPAScript";
        playerAliasScriptEntry.Flags = ScriptEntry.Flag.Local;

        ScriptObjectProperty loaderQuestActiveProperty = new ScriptObjectProperty() { Name = "TextureScriptActive", Flags = ScriptProperty.Flag.Edited };
        loaderQuestActiveProperty.Object.SetTo(gEnableTextureLoaderScript);
        playerAliasScriptEntry.Properties.Add(loaderQuestActiveProperty);

        ScriptObjectProperty verboseModeProperty = new ScriptObjectProperty() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited };
        verboseModeProperty.Object.SetTo(gTextureLoaderVerboseMode);
        playerAliasScriptEntry.Properties.Add(verboseModeProperty);

        loaderQuestFragmentAlias.Scripts.Add(playerAliasScriptEntry);
        texLoaderScriptAdapter.Aliases.Add(loaderQuestFragmentAlias);
        texLoaderQuest.VirtualMachineAdapter = texLoaderScriptAdapter;

        // copy quest alias script
        string questAliasSourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "SynthEBD Face Texture", "SynthEBDTextureLoaderPAScript.pex");
        string questAliasDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDTextureLoaderPAScript.pex");
        _patcherIO.TryCopyResourceFile(questAliasSourcePath, questAliasDestPath, _logger);
    }
}