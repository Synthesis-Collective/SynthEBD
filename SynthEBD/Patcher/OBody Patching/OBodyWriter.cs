using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class OBodyWriter
    {
        public static Spell CreateOBodyAssignmentSpell(SkyrimMod outputMod)
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
            ScriptApplyBodySlide.Name = "SynthEBDBodySlideScript";
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

        public static void CopyBodySlideScript()
        {
            var sourcePath = "";
            switch(PatcherSettings.General.BSSelectionMode)
            {
                case BodySlideSelectionMode.OBody: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript", "OBody", "SynthEBDBodySlideScript.pex"); break;
                case BodySlideSelectionMode.AutoBodyAE: sourcePath = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "BodySlideScript", "AutoBodyAE", "SynthEBDBodySlideScript.pex"); break;
            }
            var destPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "Scripts", "SynthEBDBodySlideScript.pex");

            if (!File.Exists(sourcePath))
            {
                Logger.LogErrorWithStatusUpdate("Could not find " + sourcePath, ErrorType.Error);
                return;
            }

            try
            {
                FileInfo file = new System.IO.FileInfo(destPath);
                file.Directory.Create(); // If the directory already exists, this method does nothing.
                File.Copy(sourcePath, destPath, true);
            }
            catch
            {
                Logger.LogErrorWithStatusUpdate("Could not copy " + sourcePath + "to " + destPath, ErrorType.Error);
            }
        }

        public static void ApplyBodySlideSpell(INpcGetter npcGetter, Spell bodySlideSpell, SkyrimMod outputMod)
        {
            var npc = outputMod.Npcs.GetOrAddAsOverride(npcGetter);
            if (npc.ActorEffect == null)
            {
                npc.ActorEffect = new Noggog.ExtendedList<Mutagen.Bethesda.Plugins.IFormLinkGetter<ISpellRecordGetter>>(); 
            }
            npc.ActorEffect.Add(bodySlideSpell);
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

            var destPath = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "SynthEBD", "BodySlideDict.json");

            try
            {
                FileInfo file = new System.IO.FileInfo(destPath);
                file.Directory.Create(); // If the directory already exists, this method does nothing.
                File.WriteAllText(destPath, outputStr);
            }
            catch
            {
                Logger.LogErrorWithStatusUpdate("Could not write BodySlide assignments to " + destPath, ErrorType.Error);
            }
        }
    }
}
