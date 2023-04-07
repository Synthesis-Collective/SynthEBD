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

namespace SynthEBD;

public class FaceTextureScriptWriter
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    private readonly PatcherIO _patcherIO;
    public FaceTextureScriptWriter(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, PatcherIO patcherIO)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
        _patcherIO = patcherIO;
    }

    public (Keyword, GlobalShort, GlobalShort) InitializeToggleRecords(ISkyrimMod outputMod)
    {
        Keyword synthEBDFaceKW = outputMod.Keywords.AddNew();
        synthEBDFaceKW.EditorID = "SynthEBDProcessFace";
        var gEnableFaceTextureScript = outputMod.Globals.AddNewShort();
        gEnableFaceTextureScript.EditorID = "SynthEBD_FaceTextureScriptActive";
        gEnableFaceTextureScript.Data = Convert.ToInt16(!_patcherState.TexMeshSettings.bLegacyEBDMode);
        var gFaceTextureVerboseMode = outputMod.Globals.AddNewShort();
        gFaceTextureVerboseMode.EditorID = "SynthEBD_FaceTextureVerboseMode";
        gFaceTextureVerboseMode.Data = Convert.ToInt16(_patcherState.TexMeshSettings.bNewEBDModeVerbose);
        return (synthEBDFaceKW, gEnableFaceTextureScript, gFaceTextureVerboseMode);
    }

    public Spell CreateSynthEBDFaceTextureSpell(ISkyrimMod outputMod, Keyword kFaceTextureKeyword, GlobalShort gEnableFaceTextureScript, GlobalShort gFaceTextureVerboseMode, List<string> customEventNames)
    {
        // create MGEF first
        MagicEffect MGEFFixFaceTexture = outputMod.MagicEffects.AddNew();

        // create Spell (needed for MGEF script)
        Spell SPELFixFaceTexture = outputMod.Spells.AddNew();

        MGEFFixFaceTexture.EditorID = "SynthEBDFaceTextureMGEF";
        MGEFFixFaceTexture.Name = "Applies face texture to NPC";
        MGEFFixFaceTexture.Flags |= MagicEffect.Flag.HideInUI;
        MGEFFixFaceTexture.Flags |= MagicEffect.Flag.NoDeathDispel;
        MGEFFixFaceTexture.Archetype = new MagicEffectArchetype()
        {
            Type = MagicEffectArchetype.TypeEnum.Script
        };
        MGEFFixFaceTexture.TargetType = TargetType.Self;
        MGEFFixFaceTexture.CastType = CastType.ConstantEffect;
        MGEFFixFaceTexture.VirtualMachineAdapter = new VirtualMachineAdapter();

        ScriptEntry scriptApplyFaceTexture = new ScriptEntry() { Name = "SynthEBDFaceTextureScript" };

        ScriptObjectProperty loaderQuestActiveProperty = new() { Name = "SynthEBDFaceTextureKeyword", Flags = ScriptProperty.Flag.Edited };
        loaderQuestActiveProperty.Object.SetTo(kFaceTextureKeyword);
        scriptApplyFaceTexture.Properties.Add(loaderQuestActiveProperty);

        ScriptObjectProperty scriptActiveProperty = new() { Name = "FaceTextureScriptActive", Flags = ScriptProperty.Flag.Edited };
        scriptActiveProperty.Object.SetTo(gEnableFaceTextureScript);
        scriptApplyFaceTexture.Properties.Add(scriptActiveProperty);

        ScriptObjectProperty verboseModeProperty = new() { Name = "VerboseMode", Flags = ScriptProperty.Flag.Edited };
        verboseModeProperty.Object.SetTo(gFaceTextureVerboseMode);
        scriptApplyFaceTexture.Properties.Add(verboseModeProperty);

        ScriptObjectProperty playerRefProperty = new();
        playerRefProperty.Name = "PlayerREF";
        playerRefProperty.Flags = ScriptProperty.Flag.Edited;
        bool player = FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
        if (player)
        {
            playerRefProperty.Object.SetTo(playerRefFK);
        }
        else
        {
            _logger.LogError("Could not obtain player reference");
        }
        scriptApplyFaceTexture.Properties.Add(playerRefProperty);

        ScriptStringListProperty customTriggerEventsProperty = new();
        customTriggerEventsProperty.Name = "TriggerEventNames";
        for (int i = 0; i < customEventNames.Count; i++)
        {
            if (i == 128)
            {
                _logger.LogError("Only the first 128 trigger events for the Face Texture Script will be used due to Papyrus array size limit");
                break;
            }
            customTriggerEventsProperty.Data.Add(customEventNames[i]);
        }
        scriptApplyFaceTexture.Properties.Add(customTriggerEventsProperty);

        ScriptObjectProperty faceTextureSpellProperty = new() { Name = "SynthEBDFaceTextureSpell", Flags = ScriptProperty.Flag.Edited };
        faceTextureSpellProperty.Object.SetTo(SPELFixFaceTexture);
        scriptApplyFaceTexture.Properties.Add(faceTextureSpellProperty);

        MGEFFixFaceTexture.VirtualMachineAdapter.Scripts.Add(scriptApplyFaceTexture);

        // Edit Spell
        SPELFixFaceTexture.EditorID = "SynthEBDFaceTextureSPEL";
        SPELFixFaceTexture.Name = "Applies face texture to NPC";
        SPELFixFaceTexture.CastType = CastType.ConstantEffect;
        SPELFixFaceTexture.TargetType = TargetType.Self;
        SPELFixFaceTexture.Type = SpellType.Ability;
        SPELFixFaceTexture.EquipmentType.SetTo(Skyrim.EquipType.EitherHand);

        Effect faceTextureShellEffect = new Effect();
        faceTextureShellEffect.BaseEffect.SetTo(MGEFFixFaceTexture);
        faceTextureShellEffect.Data = new EffectData();
        SPELFixFaceTexture.Effects.Add(faceTextureShellEffect);

        return SPELFixFaceTexture;
    }

    public void CopyFaceTextureScript()
    {
        string sourcePath = Path.Combine(_environmentProvider.InternalDataPath, "EBD Code", "SynthEBD Face Texture", "SynthEBDFaceTextureScript.pex");
        string destPath = Path.Combine(_paths.OutputDataFolder, "Scripts", "SynthEBDFaceTextureScript.pex");
        _patcherIO.TryCopyResourceFile(sourcePath, destPath, _logger);
    }
}
