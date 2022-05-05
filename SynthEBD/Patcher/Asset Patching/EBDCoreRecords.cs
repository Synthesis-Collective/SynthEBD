using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class EBDCoreRecords
{
    public static void CreateCoreRecords(SkyrimMod outputMod, out Keyword EBDFaceKW, out Keyword EBDScriptKW, out Spell EBDHelperSpell)
    {
        EBDFaceKW = outputMod.Keywords.AddNew();
        EBDFaceKW.EditorID = "EBDProcessFace";

        EBDScriptKW = outputMod.Keywords.AddNew();
        EBDScriptKW.EditorID = "EBDValidScriptRace";

        var headpartKW = outputMod.Keywords.AddNew();
        headpartKW.EditorID = "EBDValidHeadPartActor";

        // flesh out
        EBDHelperSpell = outputMod.Spells.AddNew();
        EBDHelperSpell.EditorID = "SP_EBD_EBDHelperScript_attacher_SPEL";
        EBDHelperSpell.Type = SpellType.Ability;
        EBDHelperSpell.TargetType = TargetType.Self;
        //

        var EBDFaceFixEnabled = outputMod.Globals.AddNewShort();
        EBDFaceFixEnabled.EditorID = "EBDFaceFixEnabled";
        EBDFaceFixEnabled.RawFloat = 1;
        var EBDHeadEnabled = outputMod.Globals.AddNewShort();
        EBDHeadEnabled.EditorID = "EBDHeadEnabled";
        EBDHeadEnabled.RawFloat = 0;
        var EBDHeadInCombatDisabled = outputMod.Globals.AddNewShort();
        EBDHeadInCombatDisabled.EditorID = "EBDHeadInCombatDisabled";
        EBDHeadInCombatDisabled.RawFloat = 1;
        var EBDHeadSpellsEnabled = outputMod.Globals.AddNewShort();
        EBDHeadSpellsEnabled.EditorID = "EBDHeadSpellsEnabled";
        EBDHeadSpellsEnabled.RawFloat = 0;
        var EBDHelperScriptEnabled = outputMod.Globals.AddNewShort();
        EBDHelperScriptEnabled.EditorID = "EBDHelperScriptEnabled";
        EBDHelperScriptEnabled.RawFloat = 1;

        var MGEF = outputMod.MagicEffects.AddNew();
        MGEF.EditorID = "SP_EBD_EBDHelperScript_attacher_MGEF";
        MGEF.VirtualMachineAdapter = new VirtualMachineAdapter();
        MGEF.VirtualMachineAdapter.Version = 5;
        MGEF.VirtualMachineAdapter.ObjectFormat = 2;
        ScriptEntry scriptEntry = new ScriptEntry();
        scriptEntry.Name = "EBDHelperScript";
        ScriptObjectProperty prop1 = new ScriptObjectProperty();
        prop1.Name = "EBDHeadPartKeyWord";
        prop1.Flags = ScriptProperty.Flag.Edited;
        prop1.Object = headpartKW.AsLink();
        scriptEntry.Properties.Add(prop1);
        ScriptObjectProperty prop2 = new ScriptObjectProperty();
        prop2.Name = "EBDHelperMagicEffect";
        prop2.Flags = ScriptProperty.Flag.Edited;
        prop2.Object = MGEF.AsLink();
        scriptEntry.Properties.Add(prop2);
        ScriptObjectProperty prop3 = new ScriptObjectProperty();
        prop3.Name = "EBDHelperSpell";
        prop3.Flags = ScriptProperty.Flag.Edited;
        prop3.Object = EBDHelperSpell.AsLink();
        scriptEntry.Properties.Add(prop3);
        ScriptObjectProperty prop4 = new ScriptObjectProperty();
        prop4.Name = "EBDProcessFace";
        prop4.Flags = ScriptProperty.Flag.Edited;
        prop4.Object = EBDFaceKW.AsLink();
        scriptEntry.Properties.Add(prop4);
        ScriptObjectProperty prop5 = new ScriptObjectProperty();
        prop5.Name = "EBDScriptKeyWord";
        prop5.Flags = ScriptProperty.Flag.Edited;
        prop5.Object = EBDScriptKW.AsLink();
        scriptEntry.Properties.Add(prop5);
        ScriptObjectProperty prop6 = new ScriptObjectProperty();
        prop6.Name = "isHeadEnabled";
        prop6.Flags = ScriptProperty.Flag.Edited;
        prop6.Object = EBDHeadEnabled.AsLink();
        scriptEntry.Properties.Add(prop6);
        ScriptObjectProperty prop7 = new ScriptObjectProperty();
        prop7.Name = "isHeadInCombatDisabled";
        prop7.Flags = ScriptProperty.Flag.Edited;
        prop7.Object = EBDHeadInCombatDisabled.AsLink();
        scriptEntry.Properties.Add(prop7);
        ScriptObjectProperty prop8 = new ScriptObjectProperty();
        prop8.Name = "isHeadSpellsEnabled";
        prop8.Flags = ScriptProperty.Flag.Edited;
        prop8.Object = EBDHeadSpellsEnabled.AsLink();
        scriptEntry.Properties.Add(prop8);
        ScriptObjectProperty prop9 = new ScriptObjectProperty();
        prop9.Name = "isScriptEnabled";
        prop9.Flags = ScriptProperty.Flag.Edited;
        prop9.Object = EBDHelperScriptEnabled.AsLink();
        scriptEntry.Properties.Add(prop9);
        ScriptObjectProperty prop10 = new ScriptObjectProperty();
        prop10.Name = "PlayerREF";
        prop10.Flags = ScriptProperty.Flag.Edited;
        bool player = FormKey.TryFactory("000014:Skyrim.esm", out FormKey playerRefFK);
        prop10.Object.SetTo(playerRefFK);
        scriptEntry.Properties.Add(prop10);
        MGEF.VirtualMachineAdapter.Scripts.Add(scriptEntry);
        MGEF.CastType = CastType.ConstantEffect;
        MGEF.Flags |= MagicEffect.Flag.HideInUI;
        MGEF.Flags |= MagicEffect.Flag.NoDeathDispel;

        Effect spellEffect = new Effect();
        spellEffect.BaseEffect = MGEF.AsNullableLink();
        spellEffect.Data = new EffectData();
        spellEffect.Data.Magnitude = 0;
        spellEffect.Data.Duration = 0;
        spellEffect.Data.Area = 0;
        EBDHelperSpell.Effects.Add(spellEffect);
    }

    public static void ApplyHelperSpell(SkyrimMod outputMod, Spell EBDHelperSpell)
    {
        foreach (var raceGetter in PatcherEnvironmentProvider.Environment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<IRaceGetter>())
        {
            if (PatcherSettings.General.PatchableRaces.Contains(raceGetter.FormKey))
            {
                var patchableRace = outputMod.Races.GetOrAddAsOverride(raceGetter);
                if (patchableRace.ActorEffect == null)
                {
                    patchableRace.ActorEffect = new Noggog.ExtendedList<IFormLinkGetter<ISpellRecordGetter>>();
                }
                patchableRace.ActorEffect.Add(EBDHelperSpell);
            }
        }
    }

        
    public static Keyword CreateHeadPartKeyword(SkyrimMod outputMod)
    {
        var headPartKW = outputMod.Keywords.AddNew();
        headPartKW.EditorID = "EBDValidHeadPartActor";
        return headPartKW;
    }

    public static void ApplyHeadPartKeyword(HashSet<Npc> headPartNPCs, Keyword headPartKeyword)
    {
        foreach (var Npc in headPartNPCs)
        {
            Npc.Keywords.Add(headPartKeyword);
        }
    }
}