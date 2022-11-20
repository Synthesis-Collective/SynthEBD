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
        public static Spell CreateHeadPartAssignmentSpell(SkyrimMod outputMod, GlobalShort settingsLoadedGlobal)
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
            MGEFApplyHeadParts.Archetype.Type = MagicEffectArchetype.TypeEnum.Script;
            MGEFApplyHeadParts.TargetType = TargetType.Self;
            MGEFApplyHeadParts.CastType = CastType.ConstantEffect;
            MGEFApplyHeadParts.VirtualMachineAdapter = new VirtualMachineAdapter();

            ScriptEntry ScriptApplyHeadParts = new ScriptEntry();
            ScriptApplyHeadParts.Name = "SynthEBDHeadPartScript";
            
            ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "SynthEBDDataBaseLoaded", Flags = ScriptProperty.Flag.Edited };
            settingsLoadedProperty.Object.SetTo(settingsLoadedGlobal);
            ScriptApplyHeadParts.Properties.Add(settingsLoadedProperty);

            ScriptObjectProperty magicEffectProperty = new ScriptObjectProperty() { Name = "SynthEBDHeadPartMGEF", Flags = ScriptProperty.Flag.Edited };
            magicEffectProperty.Object.SetTo(MGEFApplyHeadParts);
            ScriptApplyHeadParts.Properties.Add(magicEffectProperty);

            ScriptObjectProperty spellProperty = new ScriptObjectProperty() { Name = "SynthEBDHeadPartSpell", Flags = ScriptProperty.Flag.Edited };
            spellProperty.Object.SetTo(SPELApplyHeadParts);
            ScriptApplyHeadParts.Properties.Add(spellProperty);

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

        public static void CreateHeadPartLoaderQuest(SkyrimMod outputMod, GlobalShort settingsLoadedGlobal)
        {
            Quest bsLoaderQuest = outputMod.Quests.AddNew();
            bsLoaderQuest.Name = "Loads SynthEBD Head Part Assignments";
            bsLoaderQuest.EditorID = "SynthEBDHPLoaderQuest";

            bsLoaderQuest.Flags |= Quest.Flag.StartGameEnabled;
            bsLoaderQuest.Flags |= Quest.Flag.RunOnce;

            QuestAlias playerQuestAlias = new QuestAlias();
            FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
            playerQuestAlias.ForcedReference.SetTo(playerRefFK);
            bsLoaderQuest.Aliases.Add(playerQuestAlias);

            QuestAdapter bsLoaderScriptAdapter = new QuestAdapter();

            ScriptEntry bsLoaderScriptEntry = new ScriptEntry() { Name = "SynthEBDHeadPartLoaderQuestScript", Flags = ScriptEntry.Flag.Local };
            ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "SynthEBDDataBaseLoaded", Flags = ScriptProperty.Flag.Edited };
            settingsLoadedProperty.Object.SetTo(settingsLoadedGlobal.FormKey);
            bsLoaderScriptEntry.Properties.Add(settingsLoadedProperty);
            bsLoaderScriptAdapter.Scripts.Add(bsLoaderScriptEntry);

            QuestFragmentAlias loaderQuestFragmentAlias = new QuestFragmentAlias();
            loaderQuestFragmentAlias.Property = new ScriptObjectProperty() { Name = "000 Player" };
            loaderQuestFragmentAlias.Property.Object.SetTo(bsLoaderQuest.FormKey);
            loaderQuestFragmentAlias.Property.Name = "Player";
            loaderQuestFragmentAlias.Property.Alias = 0;

            ScriptEntry playerAliasScriptEntry = new ScriptEntry();
            playerAliasScriptEntry.Name = "SynthEBDHeadPartLoaderPAScript";
            playerAliasScriptEntry.Flags = ScriptEntry.Flag.Local;
            ScriptObjectProperty loaderQuestProperty = new ScriptObjectProperty() { Name = "QuestScript", Flags = ScriptProperty.Flag.Edited };
            loaderQuestProperty.Object.SetTo(bsLoaderQuest.FormKey);

            playerAliasScriptEntry.Properties.Add(loaderQuestProperty);
            loaderQuestFragmentAlias.Scripts.Add(playerAliasScriptEntry);
            bsLoaderScriptAdapter.Aliases.Add(loaderQuestFragmentAlias);
            bsLoaderQuest.VirtualMachineAdapter = bsLoaderScriptAdapter;

            // copy quest script
            string questSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "HeadPartScripts", "SynthEBDHeadPartLoaderQuestScript.pex");
            string questDestPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDHeadPartLoaderQuestScript.pex");
            PatcherIO.TryCopyResourceFile(questSourcePath, questDestPath);
            // copy quest alias script
            string questAliasSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "HeadPartScripts", "SynthEBDHeadPartLoaderPAScript.pex");
            string questAliasDestPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDHeadPartLoaderPAScript.pex");
            PatcherIO.TryCopyResourceFile(questAliasSourcePath, questAliasDestPath);
            // copy Seq file
            QuestInit.WriteQuestSeqFile();
        }

        public static void CopyHeadPartScript()
        {
            var sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "HeadPartScripts", "SynthEBDHeadPartScript.pex");
            var destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDHeadPartScript.pex");
            PatcherIO.TryCopyResourceFile(sourcePath, destPath);
        }

        /*
        public static void WriteHeadPartSPIDIni(Spell headPartSpell)
        {
            string str = "Spell = " + headPartSpell.FormKey.ToString().Replace(":", " - ") + " | ActorTypeNPC | NONE | NONE | "; // original format - SPID auto-updates but this is compatible with old SPID versions
            string outputPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDHeadPartDistributor_DISTR.ini");
            Task.Run(() => PatcherIO.WriteTextFile(outputPath, str));
        }
        */
        public static void WriteAssignmentDictionary()
        {
            if (Patcher.HeadPartTracker.Count == 0)
            {
                Logger.LogMessage("No head parts were assigned to any NPCs");
                return;
            }

            // remove NPCs for which no headparts were assigned
            Dictionary<FormKey, HeadPartSelection> populatedHeadPartTracker = new();
            foreach (var entry in Patcher.HeadPartTracker)
            {
                var assignments = entry.Value;
                if (assignments.Beard != null || assignments.Brows != null || assignments.Eyes != null || assignments.Face != null || assignments.Hair != null || assignments.Misc != null || assignments.Scars != null)
                {
                    populatedHeadPartTracker.Add(entry.Key, entry.Value);
                }
            }

            var outputDictionaries = DictionarySplitter<FormKey, HeadPartSelection>.SplitDictionary(populatedHeadPartTracker, 176); // split dictionary into sub-dictionaries due to apparent JContainers json size limit

            int dictIndex = 0;
            foreach (var dict in outputDictionaries)
            {
                dictIndex++;
                string outputStr = JSONhandler<Dictionary<FormKey, HeadPartSelection>>.Serialize(dict, out bool success, out string exception);
                if (!success)
                {
                    Logger.LogError("Could not save head part assignment dictionary " + dictIndex + ". See log.");
                    Logger.LogMessage("Could not save head part assigment dictionary " + dictIndex + ". Error:");
                    Logger.LogMessage(exception);
                    return;
                }

                var destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBD", "HeadPartDict" + dictIndex + ".json");

                try
                {
                    PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
                    File.WriteAllText(destPath, outputStr);
                }
                catch
                {
                    Logger.LogErrorWithStatusUpdate("Could not write Head Part assignments to " + destPath, ErrorType.Error);
                }
            }
        }
        public static void CleanPreviousOutputs()
        {
            var outputDir = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBD");
            if (!Directory.Exists(outputDir)) { return; }

            var oldFiles = Directory.GetFiles(outputDir).Where(x => Path.GetFileName(x).StartsWith("HeadPartDict"));
            foreach (var path in oldFiles)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        Logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                    }
                }
            }
        }
    }
}
