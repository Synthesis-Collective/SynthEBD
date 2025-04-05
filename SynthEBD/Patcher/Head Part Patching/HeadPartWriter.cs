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
    public class HeadPartWriter
    {
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly PatcherIO _patcherIO;
        public HeadPartWriter(IEnvironmentStateProvider environmentProvider, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO)
        {
            _environmentProvider = environmentProvider;
            _logger = logger;
            _paths = paths;
            _patcherIO = patcherIO;
        }
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
        public void WriteAssignmentDictionary(Dictionary<NPCInfo, Dictionary<HeadPart.TypeEnum, FormKey>> assignedHeadPartTransfers)
        {
            if (!assignedHeadPartTransfers.Any())
            {
                _logger.LogMessage("No head parts were assigned to any NPCs");
                return;
            }

            var outputDictionary = new Dictionary<string, Dictionary<HeadPart.TypeEnum, FormKey?>>();
            foreach (var entry in assignedHeadPartTransfers)
            {
                outputDictionary.TryAdd(entry.Key.NPC.FormKey.ToJContainersCompatiblityKey(), GetFullHeadPartSet(entry.Value));
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
    }
}
