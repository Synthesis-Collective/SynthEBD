using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using System.IO;

namespace SynthEBD;

public class OBodyWriter
{
    public static void CreateSynthEBDDomain()
    {
        string domainPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "SKSE","Plugins","JCData","Domains","PSM_SynthEBD");
        Directory.CreateDirectory(domainPath);

        string domainScriptPath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "JContainers Domain", "PSM_SynthEBD.pex");
        string domainScriptDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "PSM_SynthEBD.pex");
        TryCopyResourceFile(domainScriptPath, domainScriptDestPath);
    }

    public static Spell CreateOBodyAssignmentSpell(SkyrimMod outputMod, GlobalShort settingsLoadedGlobal)
    {
        // create MGEF first
        MagicEffect MGEFApplyBodySlide = outputMod.MagicEffects.AddNew();
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
        ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "loadingCompleted", Flags = ScriptProperty.Flag.Edited } ;
        settingsLoadedProperty.Object.SetTo(settingsLoadedGlobal);
        ScriptApplyBodySlide.Properties.Add(settingsLoadedProperty);

        MGEFApplyBodySlide.VirtualMachineAdapter.Scripts.Add(ScriptApplyBodySlide);

        // create Spell
        Spell SPELApplyBodySlide = outputMod.Spells.AddNew();
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
        ScriptObjectProperty settingsLoadedProperty = new ScriptObjectProperty() { Name = "loadingCompleted", Flags = ScriptProperty.Flag.Edited };
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

        // copy quest script
        string questSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideQuest", "SynthEBDBodySlideLoaderQuestScript.pex");
        string questDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "SynthEBDBodySlideLoaderQuestScript.pex");
        TryCopyResourceFile(questSourcePath, questDestPath);
        // copy quest alias script
        string questAliasSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideQuest", "SynthEBDBodySlideLoaderPAScript.pex");
        string questAliasDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "SynthEBDBodySlideLoaderPAScript.pex");
        TryCopyResourceFile(questAliasSourcePath, questAliasDestPath);
        // copy Seq file
        string questSeqSourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideQuest", "SynthEBD.seq");
        string questSeqDestPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Seq", "SynthEBD.seq");
        TryCopyResourceFile(questSeqSourcePath, questSeqDestPath);
    }

    public static void CopyBodySlideScript()
    {
        var sourcePath = "";
        var destPath = "";
        switch(PatcherSettings.General.BSSelectionMode)
        {
            case BodySlideSelectionMode.OBody:
                switch(PatcherSettings.OBody.UseVerboseScripts)
                {
                    case false: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript", "OBody", "SynthEBDBodySlideScriptOBody.pex"); break;
                    case true: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript - Verbose Versions", "OBody", "SynthEBDBodySlideScriptOBody.pex"); break;
                }
                    
                destPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "SynthEBDBodySlideScriptOBody.pex");
                break;
            case BodySlideSelectionMode.AutoBody:
                switch (PatcherSettings.OBody.UseVerboseScripts)
                {
                    case false: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript", "AutoBody", "SynthEBDBodySlideScriptAutoBody.pex"); break;
                    case true: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript - Verbose Versions", "AutoBody", "SynthEBDBodySlideScriptAutoBody.pex"); break;
                }
                destPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "Scripts", "SynthEBDBodySlideScriptAutoBody.pex");
                break;
        }

        TryCopyResourceFile(sourcePath, destPath);
    }

    public static void WriteBodySlideSPIDIni(Spell bodySlideSpell, Settings_OBody obodySettings, SkyrimMod outputMod)
    {
        string str = "Spell = " + bodySlideSpell.FormKey.ToString().Replace(":", " - ") + " | ActorTypeNPC | NONE | NONE | "; // original format - SPID auto-updates but this is compatible with old SPID versions

        bool hasMaleBodySlides = obodySettings.BodySlidesMale.Where(x => obodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any();
        bool hasFemaleBodySlides = obodySettings.BodySlidesFemale.Where(x => obodySettings.CurrentlyExistingBodySlides.Contains(x.Label)).Any();

        if (!hasMaleBodySlides && !hasFemaleBodySlides) { return; }
        else if (hasMaleBodySlides && !hasFemaleBodySlides) { str += "M"; }
        else if (!hasMaleBodySlides && hasFemaleBodySlides) { str += "F"; }

        string outputPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini");
        Task.Run(() => PatcherIO.WriteTextFile(outputPath, str));
    }

    public static void WriteAssignmentDictionary()
    {
        if (Patcher.BodySlideTracker.Count == 0)
        {
            Logger.LogMessage("No BodySlides were assigned to any NPCs");
            return;
        }

        string outputStr = "{\n\t\"__metaInfo\": {\n\t\t\"typeName\": \"JFormMap\"\n\t}";

        foreach (var entry in Patcher.BodySlideTracker)
        {
            outputStr += ",\n\t\"__formData|" + entry.Key.ModKey.FileName + "|0x" + entry.Key.IDString().TrimStart('0') + "\": \"" + entry.Value + "\"";
        }
        outputStr += "}";

        var destPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "SynthEBD", "BodySlideDict.json");

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

        var destPath = Path.Combine(PatcherSettings.General.OutputDataFolder, "autoBody", "Config", "morphs.ini");

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
            Path.Combine(PatcherSettings.General.OutputDataFolder, "autoBody", "Config", "morphs.ini"),
            Path.Combine(PatcherSettings.General.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", PatcherSettings.General.PatchFileName, "morphs.ini")
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
                    Logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                }
            }
        }    
    }

    public static void ClearOutputForIniMode()
    {
        HashSet<string> toClear = new HashSet<string>()
        {
            Path.Combine(PatcherSettings.General.OutputDataFolder, "SynthEBD", "BodySlideDict.json"),
            Path.Combine(PatcherSettings.General.OutputDataFolder, "SynthEBDBodySlideDistributor_DISTR.ini"),
            Path.Combine(PatcherSettings.General.OutputDataFolder, "Meshes", "actors", "character", "BodyGenData", PatcherSettings.General.PatchFileName, "morphs.ini")
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
                    Logger.LogErrorWithStatusUpdate("Could not delete file at " + path, ErrorType.Warning);
                }
            }
        }
    }

    public static void TryCopyResourceFile(string sourcePath, string destPath)
    {
        if (!File.Exists(sourcePath))
        {
            Logger.LogErrorWithStatusUpdate("Could not find " + sourcePath, ErrorType.Error);
            return;
        }

        try
        {
            PatcherIO.CreateDirectoryIfNeeded(destPath, PatcherIO.PathType.File);
            File.Copy(sourcePath, destPath, true);
        }
        catch
        {
            Logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
        }
    }
}