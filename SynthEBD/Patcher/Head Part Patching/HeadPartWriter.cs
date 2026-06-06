using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Emits the head-part runtime artifacts: the SKSE spell/magic effect and loader quest that apply head-part
    /// assignments in-game, the supporting Papyrus scripts, the script-mode <c>HeadPartAssignments.json</c>
    /// dictionary, and (for Nif mode) direct head-part edits onto NPC or surrogate records. Runs as part of the
    /// head-part stage of the patching pipeline.
    /// </summary>
    public class HeadPartWriter
    {
        private readonly IOutputEnvironmentStateProvider _environmentProvider;
        PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        private readonly SurrogateNPCProvider _surrogateNpcProvider;
        /// <summary>Creates a writer with the output environment, patcher state, paths, IO helper, and surrogate-NPC provider used to emit head-part records and data files.</summary>
        public HeadPartWriter(IOutputEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO, SurrogateNPCProvider surrogateNpcProvider)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _paths = paths;
            _patcherIO = patcherIO;
            _surrogateNpcProvider = surrogateNpcProvider;
        }
        /// <summary>
        /// Creates the constant-effect spell + scripted magic effect (<c>SynthEBDHeadPartScript</c>) that applies head-part
        /// assignments to its target NPC, wiring the verbose-mode global into the script. Side effect: adds new MGEF and
        /// SPEL records to <paramref name="outputMod"/>.
        /// </summary>
        /// <param name="gHeadpartsVerboseMode">Global toggling verbose in-game logging for the head-part script.</param>
        /// <returns>The created head-part-application spell.</returns>
        public static Spell CreateHeadPartAssignmentSpell(ISkyrimMod outputMod, GlobalShort gHeadpartsVerboseMode)
        {
            // create MGEF
            MagicEffect MGEFApplyHeadParts = outputMod.MagicEffects.AddNew();

            // create Spell (needed for MGEF script)
            Spell SPELApplyHeadParts = outputMod.Spells.AddNew();

            // edit MGEF
            MGEFApplyHeadParts.EditorID = "SynthEBDHeadPartMGEF";
            MGEFApplyHeadParts.Name = "Applies head part assignment to NPC";
            MGEFApplyHeadParts.Flags |= MagicEffect.Flag.HideInUI;
            MGEFApplyHeadParts.Flags |= MagicEffect.Flag.NoDeathDispel;
            MGEFApplyHeadParts.Archetype = new MagicEffectArchetype()
            {
                Type = MagicEffectArchetype.TypeEnum.Script
            };
            MGEFApplyHeadParts.TargetType = TargetType.Self;
            MGEFApplyHeadParts.CastType = CastType.ConstantEffect;
            MGEFApplyHeadParts.VirtualMachineAdapter = new VirtualMachineAdapter();

            ScriptEntry ScriptApplyHeadParts = new ScriptEntry();
            ScriptApplyHeadParts.Name = "SynthEBDHeadPartScript";
            
            ScriptObjectProperty verboseModeProperty = new ScriptObjectProperty() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited };
            verboseModeProperty.Object.SetTo(gHeadpartsVerboseMode);
            ScriptApplyHeadParts.Properties.Add(verboseModeProperty);

            MGEFApplyHeadParts.VirtualMachineAdapter.Scripts.Add(ScriptApplyHeadParts);

            // Edit Spell
            SPELApplyHeadParts.EditorID = "SynthEBDHeadPartSPEL";
            SPELApplyHeadParts.Name = "Applies head part assignment to NPC";
            SPELApplyHeadParts.CastType = CastType.ConstantEffect;
            SPELApplyHeadParts.TargetType = TargetType.Self;
            SPELApplyHeadParts.Type = SpellType.Ability;
            SPELApplyHeadParts.EquipmentType.SetTo(Skyrim.EquipType.EitherHand);

            Effect HeadPartShellEffect = new Effect();
            HeadPartShellEffect.BaseEffect.SetTo(MGEFApplyHeadParts);
            HeadPartShellEffect.Data = new EffectData();
            SPELApplyHeadParts.Effects.Add(HeadPartShellEffect);

            return SPELApplyHeadParts;
        }

        /// <summary>
        /// Creates the start-game-enabled, run-once loader quest (forced-referenced to the player) whose player-alias
        /// script bootstraps the head-part system at game start. Side effects: adds a new Quest record to
        /// <paramref name="outputMod"/> and copies the loader player-alias .pex script into the output Scripts folder.
        /// </summary>
        /// <param name="gEnableHeadParts">Global gating whether the head-part loader script runs.</param>
        /// <param name="gHeadpartsVerboseMode">Global toggling verbose in-game logging.</param>
        public void CreateHeadPartLoaderQuest(ISkyrimMod outputMod, GlobalShort gEnableHeadParts, GlobalShort gHeadpartsVerboseMode)
        {
            Quest hpLoaderQuest = outputMod.Quests.AddNew();
            hpLoaderQuest.Name = "Loads SynthEBD Head Part Assignments";
            hpLoaderQuest.EditorID = "SynthEBDHPLoaderQuest";

            hpLoaderQuest.Flags |= Quest.Flag.StartGameEnabled;
            hpLoaderQuest.Flags |= Quest.Flag.RunOnce;

            QuestAlias playerQuestAlias = new QuestAlias();
            FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
            playerQuestAlias.ForcedReference.SetTo(playerRefFK);
            hpLoaderQuest.Aliases.Add(playerQuestAlias);

            QuestAdapter hpLoaderScriptAdapter = new QuestAdapter();

            QuestFragmentAlias loaderQuestFragmentAlias = new QuestFragmentAlias();
            loaderQuestFragmentAlias.Property = new ScriptObjectProperty() { Name = "000 Player" };
            loaderQuestFragmentAlias.Property.Object.SetTo(hpLoaderQuest);
            loaderQuestFragmentAlias.Property.Name = "Player";
            loaderQuestFragmentAlias.Property.Alias = 0;

            ScriptEntry playerAliasScriptEntry = new ScriptEntry();
            playerAliasScriptEntry.Name = "SynthEBDHeadPartLoaderPAScript";
            playerAliasScriptEntry.Flags = ScriptEntry.Flag.Local;

            ScriptObjectProperty loaderQuestActiveProperty = new ScriptObjectProperty() { Name = "HeadPartScriptActive", Flags = ScriptProperty.Flag.Edited };
            loaderQuestActiveProperty.Object.SetTo(gEnableHeadParts);
            playerAliasScriptEntry.Properties.Add(loaderQuestActiveProperty);

            ScriptObjectProperty verboseModeProperty = new ScriptObjectProperty() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited };
            verboseModeProperty.Object.SetTo(gHeadpartsVerboseMode);
            playerAliasScriptEntry.Properties.Add(verboseModeProperty);

            loaderQuestFragmentAlias.Scripts.Add(playerAliasScriptEntry);
            hpLoaderScriptAdapter.Aliases.Add(loaderQuestFragmentAlias);
            hpLoaderQuest.VirtualMachineAdapter = hpLoaderScriptAdapter;

            // copy quest alias script
            string questAliasSourcePath = Path.Combine(_environmentProvider.InternalDataPath, "HeadPartScripts", "SynthEBDHeadPartLoaderPAScript.pex");
            string questAliasDestPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDHeadPartLoaderPAScript.pex");
            _patcherIO.TryCopyResourceFile(questAliasSourcePath, questAliasDestPath, _logger);
        }

        /// <summary>Copies the main <c>SynthEBDHeadPartScript.pex</c> from internal data into the output Scripts folder.</summary>
        public void CopyHeadPartScript()
        {
            var sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "HeadPartScripts", "SynthEBDHeadPartScript.pex");
            var destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDHeadPartScript.pex");
            _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
        }

        /*
        public static void WriteHeadPartSPIDIni(Spell headPartSpell)
        {
            string str = "Spell = " + headPartSpell.FormKey.ToString().Replace(":", " - ") + " | ActorTypeNPC | NONE | NONE | "; // original format - SPID auto-updates but this is compatible with old SPID versions
            string outputPath = Path.Combine(_paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini");
            Task.Run(() => PatcherIO.WriteTextFile(outputPath, str));
        }
        */
        /// <summary>
        /// Writes the script-mode head-part assignments as <c>SynthEBD/HeadPartAssignments.json</c>, a JContainers-keyed
        /// map of NPC FormKey to a full per-type head-part set (missing types filled with null). No-ops with a log message
        /// when nothing was assigned. Side effect: writes the JSON file; logs and returns on serialization failure.
        /// </summary>
        /// <param name="assignedHeadPartTransfers">Per-NPC assigned head parts to serialize.</param>
        public void WriteAssignmentDictionary(Dictionary<FormKey, (NPCInfo NpcInfo, Dictionary<HeadPart.TypeEnum, FormKey> HeadParts)> assignedHeadPartTransfers)
        {
            if (!assignedHeadPartTransfers.Any())
            {
                _logger.LogMessage("No head parts were assigned to any NPCs");
                return;
            }

            var outputDictionary = new Dictionary<string, Dictionary<HeadPart.TypeEnum, FormKey?>>();
            foreach (var entry in assignedHeadPartTransfers)
            {
                outputDictionary.TryAdd(entry.Key.ToJContainersCompatiblityKey(), GetFullHeadPartSet(entry.Value.HeadParts));
            }
            string outputStr = JSONhandler<Dictionary<string, Dictionary<HeadPart.TypeEnum, FormKey?>>>.Serialize(outputDictionary, out bool success, out string exception);
            if (!success)
            {
                _logger.LogError("Could not save head part assignment dictionary. See log.");
                _logger.LogMessage("Could not save head part assigment dictionary. Error:");
                _logger.LogMessage(exception);
                return;
            }

            var destPath = Path.Combine(_paths.OutputDataFolder, "SynthEBD", "HeadPartAssignments.json");

            try
            {
                PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
                File.WriteAllText(destPath, outputStr);
            }
            catch
            {
                _logger.LogErrorWithStatusUpdate("Could not write Head Part assignments to " + destPath, ErrorType.Error);
            }
        }

        /// <summary>
        /// Expands a sparse set of <paramref name="assignments"/> into a full per-type dictionary covering every
        /// <see cref="HeadPart.TypeEnum"/> slot, leaving unassigned types null (see <see cref="GetBlankHeadPartAssignment"/>).
        /// </summary>
        public Dictionary<HeadPart.TypeEnum, FormKey?> GetFullHeadPartSet(
            Dictionary<HeadPart.TypeEnum, FormKey> assignments)
        {
            var output = GetBlankHeadPartAssignment();
            foreach (var type in output.Keys)
            {
                if (assignments.ContainsKey(type))
                {
                    output[type] = assignments[type];
                }
            }
            
            return output;
        }
        
        /// <summary>Returns a fresh head-part dictionary with every supported <see cref="HeadPart.TypeEnum"/> slot present and set to null.</summary>
        public static Dictionary<HeadPart.TypeEnum, FormKey?> GetBlankHeadPartAssignment()
        {
            return new Dictionary<HeadPart.TypeEnum, FormKey?>()
            {
                { HeadPart.TypeEnum.Eyebrows, null },
                { HeadPart.TypeEnum.Eyes, null },
                { HeadPart.TypeEnum.Face, null },
                { HeadPart.TypeEnum.FacialHair, null },
                { HeadPart.TypeEnum.Hair, null },
                { HeadPart.TypeEnum.Misc, null },
                { HeadPart.TypeEnum.Scars, null }
            };
        }
        
        /// <summary>Deletes stale head-part output files (those named <c>HeadPartDict*</c>) from the output SynthEBD folder. Side effect: deletes files.</summary>
        public void CleanPreviousOutputs()
        {
            var outputDir = Path.Combine(_paths.OutputDataFolder, "SynthEBD");
            if (!Directory.Exists(outputDir)) { return; }

            var oldFiles = Directory.GetFiles(outputDir).Where(x => Path.GetFileName(x).StartsWith("HeadPartDict")).ToArray();
            foreach (var path in oldFiles)
            {
                _patcherIO.TryDeleteFile(path, _logger);
            }
        }

        // This will need to be updated to respect the "mutliple allowed" vs "only one allowed" rules for each headpart
        // type. For now, let's start with just applying one of each
        
        /// <summary>
        /// Applies head part assignments to the NPC record.
        /// 
        /// Routing depends on Headpart Patching Mode and SkyPatcher mode:
        ///   - Nif + No SkyPatcher (Cases 3, 7, 11): edit original NPC record directly
        ///   - Nif + SkyPatcher (Cases 4, 8, 16): edit surrogate NPC record
        ///   - Script mode: edit surrogate NPC record (existing behavior, but this  
        ///     method is only called for Nif mode per the unified FaceGen loop)
        /// </summary>
        public void ApplyHeadPartRecords(NPCInfo npcInfo,
            Dictionary<HeadPart.TypeEnum, FormKey> headPartAssignments)
        {
            Npc npc;
            if (_patcherState.HeadPartSettings.PatchingMode == HeadPartPatchingMode.NifEdit)
            {
                if (_patcherState.HeadPartSettings.bSkyPatcherModeHeadparts)
                {
                    // Nif + SkyPatcher: edit the surrogate's headpart records
                    if (!_surrogateNpcProvider.TryGetSurrogateNpc(npcInfo.OriginalNPC, out npc))
                    {
                        _logger.LogMessage("WARNING: Could not get surrogate for headpart records on NPC " +
                                           npcInfo.LogIDstring + ". Falling back to direct override.");
                        npc = _environmentProvider.OutputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
                    }
                }
                else
                {
                    // Nif without SkyPatcher: edit the original NPC directly
                    npc = _environmentProvider.OutputMod.Npcs.GetOrAddAsOverride(npcInfo.OriginalNPC);
                }
            }
            else
            {
                // Script mode: always uses surrogate
                if (!_surrogateNpcProvider.TryGetSurrogateNpc(npcInfo.OriginalNPC, out npc))
                {
                    _logger.LogMessage("WARNING: Could not create surrogate for headpart script assignment on NPC " +
                                       npcInfo.LogIDstring + ". Headpart records will not be applied.");
                    return;
                }
            }

            if (npc != null)
            {
                // Figure out which headparts of each type the NPC already has
                Dictionary<HeadPart.TypeEnum, HashSet<IFormLinkGetter<IHeadPartGetter>>> existingHeadparts = new();
                foreach (var hp in npc.HeadParts)
                {
                    if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(hp.FormKey,
                            out var headPartGetter) && headPartGetter != null)
                    {
                        if (headPartGetter.Type == null)
                        {
                            continue;
                        }

                        if (!existingHeadparts.ContainsKey(headPartGetter.Type.Value))
                        {
                            existingHeadparts.Add(headPartGetter.Type.Value, new ());
                        }
                        
                        existingHeadparts[headPartGetter.Type.Value].Add(hp);
                    }
                }
                
                // Add or replace assigned headparts
                foreach (var entry in headPartAssignments)
                {
                    if (existingHeadparts.TryGetValue(entry.Key, out var existingHeadPartsForType))
                    {
                        npc.HeadParts.RemoveAll(x => existingHeadPartsForType.Contains(x));
                    }

                    npc.HeadParts.Add(entry.Value);
                }
            }
        }
    }
}