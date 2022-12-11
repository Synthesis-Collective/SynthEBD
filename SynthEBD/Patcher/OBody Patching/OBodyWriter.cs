using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System.IO;

namespace SynthEBD;

public class OBodyWriter
{
    public static Spell CreateOBodyAssignmentSpell(SkyrimMod outputMod, GlobalShort settingsLoadedGlobal)
    {
        // create MGEF first
        MagicEffect MGEFApplyBodySlide = outputMod.MagicEffects.AddNew();

        // create Spell (needed for MGEF script)
        Spell SPELApplyBodySlide = outputMod.Spells.AddNew();

        MGEFApplyBodySlide.EditorID = "SynthEBDBodySlideMGEF";
        MGEFApplyBodySlide.Name = "Applies BodySlide assignment to NPC";
        MGEFApplyBodySlide.Flags |= MagicEffect.Flag.HideInUI;
        MGEFApplyBodySlide.Flags |= MagicEffect.Flag.NoDeathDispel;
        MGEFApplyBodySlide.Archetype.Type = MagicEffectArchetype.TypeEnum.Script;
        MGEFApplyBodySlide.TargetType = TargetType.Self;
        MGEFApplyBodySlide.CastType = CastType.ConstantEffect;
        MGEFApplyBodySlide.VirtualMachineAdapter = new VirtualMachineAdapter();

        ScriptEntry ScriptApplyBodySlide = new ScriptEntry();
        switch(PatcherSettings.General.BSSelectionMode)
        {
            case BodySlideSelectionMode.OBody: ScriptApplyBodySlide.Name = "SynthEBDBodySlideScriptOBody"; break;
            case BodySlideSelectionMode.AutoBody: ScriptApplyBodySlide.Name = "SynthEBDBodySlideScriptAutoBody"; break;
        }

        ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "SynthEBDDataBaseLoaded", Flags = ScriptProperty.Flag.Edited } ;
        settingsLoadedProperty.Object.SetTo(settingsLoadedGlobal);
        ScriptApplyBodySlide.Properties.Add(settingsLoadedProperty);

        ScriptObjectProperty magicEffectProperty = new ScriptObjectProperty() { Name = "SynthEBDBodySlideMGEF", Flags = ScriptProperty.Flag.Edited };
        magicEffectProperty.Object.SetTo(MGEFApplyBodySlide);
        ScriptApplyBodySlide.Properties.Add(magicEffectProperty);

        ScriptObjectProperty spellProperty = new ScriptObjectProperty() { Name = "SynthEBDBodySlideSpell", Flags = ScriptProperty.Flag.Edited };
        spellProperty.Object.SetTo(SPELApplyBodySlide);
        ScriptApplyBodySlide.Properties.Add(spellProperty);

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

    public static void CreateBodySlideLoaderQuest(SkyrimMod outputMod, GlobalShort settingsLoadedGlobal)
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

        ScriptEntry bsLoaderScriptEntry = new ScriptEntry() { Name = "SynthEBDBodySlideLoaderQuestScript", Flags = ScriptEntry.Flag.Local };
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
        playerAliasScriptEntry.Name = "SynthEBDBodySlideLoaderPAScript";
        playerAliasScriptEntry.Flags = ScriptEntry.Flag.Local;
        ScriptObjectProperty loaderQuestProperty = new ScriptObjectProperty() { Name = "QuestScript", Flags = ScriptProperty.Flag.Edited };
        loaderQuestProperty.Object.SetTo(bsLoaderQuest.FormKey);

        playerAliasScriptEntry.Properties.Add(loaderQuestProperty);
        loaderQuestFragmentAlias.Scripts.Add(playerAliasScriptEntry);
        bsLoaderScriptAdapter.Aliases.Add(loaderQuestFragmentAlias);
        bsLoaderQuest.VirtualMachineAdapter = bsLoaderScriptAdapter;

        string scriptSourceDir = GetScriptSource();

        // copy quest script
        string questSourcePath = Path.Combine(scriptSourceDir, "Common", "SynthEBDBodySlideLoaderQuestScript.pex");
        string questDestPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideLoaderQuestScript.pex");
        PatcherIO.TryCopyResourceFile(questSourcePath, questDestPath);

        // copy quest alias script
        string questAliasSourcePath = Path.Combine(scriptSourceDir, "Common", "SynthEBDBodySlideLoaderPAScript.pex");
        string questAliasDestPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideLoaderPAScript.pex");
        PatcherIO.TryCopyResourceFile(questAliasSourcePath, questAliasDestPath);

        // copy Seq file
        QuestInit.WriteQuestSeqFile();
    }

    public static void CopyBodySlideScript()
    {
        var sourcePath = "";
        var destPath = "";
        string scriptSourceDir = GetScriptSource();

        switch (PatcherSettings.General.BSSelectionMode)
        {
            case BodySlideSelectionMode.OBody:
                sourcePath = Path.Combine(scriptSourceDir, "OBody", "SynthEBDBodySlideScriptOBody.pex");
                destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideScriptOBody.pex");
                break;
            case BodySlideSelectionMode.AutoBody:
                sourcePath = Path.Combine(scriptSourceDir, "AutoBody", "SynthEBDBodySlideScriptAutoBody.pex");
                destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Scripts", "SynthEBDBodySlideScriptAutoBody.pex");
                break;
        }

        PatcherIO.TryCopyResourceFile(sourcePath, destPath);
    }
    public static string GetScriptSource()
    {
        string scriptSourceDir = string.Empty;
        switch (PatcherSettings.OBody.UseVerboseScripts)
        {
            case false: scriptSourceDir = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScripts", "Silent"); break;
            case true: scriptSourceDir = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScripts", "Verbose"); break;
        }
        return scriptSourceDir;
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

        string outputPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini");
        Task.Run(() => PatcherIO.WriteTextFile(outputPath, str));
    }
    */

    public static void WriteAssignmentDictionary()
    {
        if (Patcher.BodySlideTracker.Count == 0)
        {
            Logger.LogMessage("No BodySlides were assigned to any NPCs");
            return;
        }

        var outputDictionaries = DictionarySplitter<FormKey, string>.SplitDictionary(Patcher.BodySlideTracker, 176); // split dictionary into sub-dictionaries due to apparent JContainers json size limit

        int dictIndex = 0;
        foreach (var dict in outputDictionaries)
        {
            dictIndex++;
            string outputStr = JSONhandler<Dictionary<FormKey, string>>.Serialize(dict, out bool success, out string exception);
            if (!success)
            {
                Logger.LogError("Could not save BodySlide assignment dictionary " + dictIndex + ". See log.");
                Logger.LogMessage("Could not save BodySlide assigment dictionary " + dictIndex + ". Error:");
                Logger.LogMessage(exception);
                return;
            }

            var destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBD", "BodySlideAssignments", "BodySlideDict" + dictIndex + ".json");

            try
            {
                PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
                File.WriteAllText(destPath, outputStr);
            }
            catch
            {
                Logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
            }
        }
    }

    public static void WriteAssignmentIni()
    {
        if (Patcher.BodySlideTracker.Count == 0)
        {
            Logger.LogMessage("No BodySlides were assigned to any NPCs");
            return;
        }

        string outputStr = "";

        foreach (var entry in Patcher.BodySlideTracker)
        {
            outputStr += BodyGenWriter.FormatFormKeyForBodyGen(entry.Key) + "=" + entry.Value + Environment.NewLine;
        }

        var destPath = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "autoBody", "Config", "morphs.ini");

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.WriteAllText(destPath, outputStr);
        }
        catch
        {
            Logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
        }
    }

    public static void ClearOutputForJsonMode()
    {
        HashSet<string> toClear = new HashSet<string>()
        {
            Path.Combine(PatcherSettings.Paths.OutputDataFolder, "autoBody", "Config", "morphs.ini"),
            Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", PatcherSettings.General.PatchFileName, "morphs.ini")
        };

        foreach (string path in toClear)
        {
            if (File.Exists(path))
            {
                PatcherIO.TryDeleteFile(path);
            }
        }

        string autoBodyDir = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "autoBody");
        if (Directory.Exists(autoBodyDir))
        {
            PatcherIO.TryDeleteDirectory(autoBodyDir);
        }
    }

    public static void ClearOutputForIniMode()
    {
        HashSet<string> toClear = new HashSet<string>()
        {
            //Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBD", "BodySlideDict.json"),
            //Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"),
            Path.Combine(PatcherSettings.Paths.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", PatcherSettings.General.PatchFileName, "morphs.ini")
        };

        var dictDir = Path.Combine(PatcherSettings.Paths.OutputDataFolder, "SynthEBD", "BodySlideAssignments");
        if (Directory.Exists(dictDir))
        {
            foreach (var file in Directory.GetFiles(dictDir))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith("BodySlideDict", StringComparison.OrdinalIgnoreCase))
                {
                    toClear.Add(file);
                }
            }
        }

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
                    Logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                }
            }
        }
    }
}