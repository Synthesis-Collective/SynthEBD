using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Z.Expressions.Compiler;

namespace SynthEBD
{
    internal static class FilePathDestinationMap
    {
        /// <summary>
        /// Destination Paths
        /// </summary>
        // head
        public const string Dest_HeadDiffuse = "HeadTexture.Diffuse.RawPath";
        public const string Dest_HeadNormal = "HeadTexture.NormalOrGloss.RawPath";
        public const string Dest_HeadSubsurface = "HeadTexture.GlowOrDetailMap.RawPath";
        public const string Dest_HeadSpecular = "HeadTexture.BacklightMaskOrSpecular.RawPath";
        public const string Dest_HeadDetail = "HeadTexture.Height.RawPath";

        // male torso
        public const string Dest_TorsoMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public const string Dest_TorsoMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public const string Dest_TorsoMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public const string Dest_TorsoMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female torso
        public const string Dest_TorsoFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public const string Dest_TorsoFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public const string Dest_TorsoFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public const string Dest_TorsoFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male handsTail
        public const string Dest_HandsMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public const string Dest_HandsMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public const string Dest_HandsMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public const string Dest_HandsMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female hands
        public const string Dest_HandsFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public const string Dest_HandsFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public const string Dest_HandsFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public const string Dest_HandsFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male feet
        public const string Dest_FeetMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public const string Dest_FeetMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public const string Dest_FeetMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public const string Dest_FeetMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female feet
        public const string Dest_FeetFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public const string Dest_FeetFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public const string Dest_FeetFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public const string Dest_FeetFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male tail
        public const string Dest_TailMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public const string Dest_TailMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public const string Dest_TailMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public const string Dest_TailMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female tail
        public const string Dest_TailFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public const string Dest_TailFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public const string Dest_TailFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public const string Dest_TailFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male TNG 
        public const string Dest_TNGMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public const string Dest_TNGMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public const string Dest_TNGMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.RawPath";
        public const string Dest_TNGMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female etc
        public const string Dest_EtcFemaleDiffusePrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.Diffuse.RawPath";
        public const string Dest_EtcFemaleDiffuseSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.Diffuse.RawPath";
        public const string Dest_EtcFemaleNormalPrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.NormalOrGloss.RawPath";
        public const string Dest_EtcFemaleNormalSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.NormalOrGloss.RawPath";
        public const string Dest_EtcFemaleSubsurfacePrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.GlowOrDetailMap.RawPath";
        public const string Dest_EtcFemaleSubsurfaceSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.GlowOrDetailMap.RawPath";
        public const string Dest_EtcFemaleSpecularPrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.BacklightMaskOrSpecular.RawPath";
        public const string Dest_EtcFemaleSpecularSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.BacklightMaskOrSpecular.RawPath";

        /// <summary>
        /// Source File Names
        /// </summary>
        /// 

        public const string Source_TNGMaleDiffuse = "malegenitals_1.dds";
        public const string Source_TNGMaleNormal = "malegenitals_1_msn.dds";
        public const string Source_TNGMaleSubsurface = "malegenitals_1_sk.dds";
        public const string Source_TNGMaleSpecular = "malegenitals_1_s.dds";

        public const string Source_TNGMaleDiffuseArgonian = "malegenitals_argonian_1.dds";
        public const string Source_TNGMaleNormalArgonian = "malegenitals_argonian_1_msn.dds";
        public const string Source_TNGMaleSpecularArgonian = "malegenitals_argonian_1_s.dds";

        public const string Source_TNGMaleDiffuseKhajiit = "malegenitals_khajiit_1.dds";
        public const string Source_TNGMaleNormalKhajiit = "malegenitals_khajiit_1_msn.dds";
        public const string Source_TNGMaleSpecularKhajiit = "malegenitals_khajiit_1_s.dds";

        public const string Source_TNGMaleNormalElder = "malegenitals_old_1_msn.dds";
        public const string Source_TNGMaleDiffuseAfflicted = "malegenitals_afflicted_1.dds";
        public const string Source_TNGMaleDiffuseSnowElf = "malegenitals_snowelf_1.dds";

        public const string Source_EtcFemaleDiffuse = "femalebody_etc_v2_1.dds";
        public const string Source_EtcFemaleNormal = "femalebody_etc_v2_1_msn.dds";
        public const string Source_EtcFemaleSubsurface = "femalebody_etc_v2_1_sk.dds";
        public const string Source_EtcFemaleSpecular = "femalebody_etc_v2_1_s.dds";

        public static readonly Dictionary<string, string> FileNameToDestMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "blankdetailmap.dds", Dest_HeadDetail }, // common to male and female

            //male head
            { "malehead.dds",  Dest_HeadDiffuse},
            { "malehead_msn.dds", Dest_HeadNormal },
            { "malehead_sk.dds",  Dest_HeadSubsurface},
            { "malehead_s.dds", Dest_HeadSpecular },
            { "maleheaddetail_age40.dds",  Dest_HeadDetail},
            { "maleheaddetail_age40rough.dds", Dest_HeadDetail },
            { "maleheaddetail_age50.dds", Dest_HeadDetail },
            { "maleheaddetail_rough01.dds", Dest_HeadDetail },
            { "maleheaddetail_rough02.dds", Dest_HeadDetail },

            //male torso
            { "malebody_1.dds", Dest_TorsoMaleDiffuse },
            { "maleBody_1_msn.dds", Dest_TorsoMaleNormal },
            { "malebody_1_sk.dds", Dest_TorsoMaleSubsurface },
            { "malebody_1_s.dds", Dest_TorsoMaleSpecular },

            //male hands
            { "malehands_1.dds", Dest_HandsMaleDiffuse },
            { "malehands_1_msn.dds", Dest_HandsMaleNormal },
            { "malehands_1_sk.dds", Dest_HandsMaleSubsurface },
            { "malehands_1_s.dds", Dest_HandsMaleSpecular },

            //male feet
            { "malebody_1_feet.dds", Dest_FeetMaleDiffuse },
            { "malebody_1_msn_feet.dds", Dest_FeetMaleNormal },
            { "malebody_1_feet_sk.dds", Dest_FeetMaleSubsurface },
            { "malebody_1_feet_s.dds", Dest_FeetMaleSpecular },

            //male vampire
            { "maleheadvampire.dds", Dest_HeadDiffuse },
            { "maleheadvampire_msn.dds", Dest_HeadNormal },
            { "maleheadorc_msn.dds", Dest_HeadNormal },

            //male afflicted
            { "maleheadafflicted.dds", Dest_HeadDiffuse },
            { "malebodyafflicted.dds", Dest_TorsoMaleDiffuse },
            { "malehandsafflicted.dds", Dest_HandsMaleDiffuse },

            //male snow elf
            { "maleheadsnowelf.dds", Dest_HeadDiffuse },
            { "malebodysnowelf.dds", Dest_TorsoMaleDiffuse },
            { "malehandssnowelf.dds", Dest_HandsMaleDiffuse },

            //male khajiit
            { "KhajiitMaleHead.dds", Dest_HeadDiffuse },
            { "KhajiitMaleHead_msn.dds", Dest_HeadNormal },
            { "khajiitmalehead_s.dds", Dest_HeadSpecular },
            { "KhajiitOld.dds", Dest_HeadDetail }, //note: used by khajiit female as well
            { "bodymale.dds", Dest_TorsoMaleDiffuse },
            { "bodymale_msn.dds", Dest_TorsoMaleNormal },
            { "bodymale_s.dds", Dest_TorsoMaleSpecular },
            { "HandsMale.dds", Dest_HandsMaleDiffuse },
            { "HandsMale_msn.dds", Dest_HandsMaleNormal },
            { "handsmale_s.dds", Dest_HandsMaleSpecular },

            //male argonian
            { "ArgonianMaleHead.dds", Dest_HeadDiffuse },
            { "ArgonianMaleHead_msn.dds", Dest_HeadNormal },
            { "ArgonianMaleHead_s.dds", Dest_HeadSpecular },
            { "ArgonianMaleHeadOld.dds", Dest_HeadDetail },
            { "argonianmalebody.dds", Dest_TorsoMaleDiffuse },
            { "argonianmalebody_msn.dds", Dest_TorsoMaleNormal },
            { "argonianmalebody_s.dds", Dest_TorsoMaleSpecular },
            { "ArgonianMaleHands.dds", Dest_HandsMaleDiffuse },
            { "ArgonianMaleHands_msn.dds", Dest_HandsMaleNormal },
            { "ArgonianMaleHands_s.dds", Dest_HandsMaleSpecular },

            //female head
            { "femalehead.dds", Dest_HeadDiffuse }, // note: used by khajiit female as well
            { "femalehead_msn.dds", Dest_HeadNormal }, // note: used by khajiit female as well
            { "femalehead_sk.dds", Dest_HeadSubsurface }, // note: used by khajiit female as well
            { "femalehead_s.dds", Dest_HeadSpecular }, // note: used by khajiit female as well
            { "femaleheaddetail_age40.dds", Dest_HeadDetail },
            { "femaleheaddetail_age50.dds", Dest_HeadDetail },
            { "femaleheaddetail_rough.dds", Dest_HeadDetail },
            { "femaleheaddetail_age40rough.dds", Dest_HeadDetail },
            { "femaleheaddetail_frekles.dds", Dest_HeadDetail },

            //female body
            { "femalebody_1.dds", Dest_TorsoFemaleDiffuse },
            { "femaleBody_1_msn.dds", Dest_TorsoFemaleNormal },
            { "femalebody_1_sk.dds", Dest_TorsoFemaleSubsurface },
            { "femalebody_1_s.dds", Dest_TorsoFemaleSpecular },

            //female hands
            { "femalehands_1.dds", Dest_HandsFemaleDiffuse },
            { "femalehands_1_msn.dds", Dest_HandsFemaleNormal },
            { "femalehands_1_sk.dds", Dest_HandsFemaleSubsurface },
            { "femalehands_1_s.dds", Dest_HandsFemaleSpecular },

            //female feet
            { "femalebody_1_feet.dds", Dest_FeetFemaleDiffuse },
            { "femalebody_1_msn_feet.dds", Dest_FeetFemaleNormal },
            { "femalebody_1_feet_sk.dds", Dest_FeetFemaleSubsurface },
            { "femalebody_1_feet_s.dds", Dest_FeetFemaleSpecular },

            //female vampire
            { "femaleheadvampire.dds", Dest_HeadDiffuse },
            { "femaleheadvampire_msn.dds", Dest_HeadNormal },
            { "femaleheadorc_msn.dds", Dest_HeadNormal },
            { "femaleheadvampire_sk.dds", Dest_HeadSubsurface },
            { "femaleheadvampire_s.dds", Dest_HeadSpecular },

            //female afflicted
            { "femaleheadafflicted.dds", Dest_HeadDiffuse },
            { "femalebodyafflicted.dds", Dest_TorsoFemaleDiffuse },
            { "femalehandsafflicted.dds", Dest_HandsFemaleDiffuse },

            //female khajiit
            { "femalebody.dds", Dest_TorsoFemaleDiffuse },
            { "femalebody_msn.dds", Dest_TorsoFemaleNormal },
            { "femalebody_s.dds", Dest_TorsoFemaleSpecular },
            { "femalehands.dds", Dest_HandsFemaleDiffuse },
            { "femalehands_msn.dds", Dest_HandsFemaleNormal },
            { "femalehands_s.dds", Dest_HandsFemaleSpecular },

            //female argonian
            { "argonianfemalehead.dds", Dest_HeadDiffuse },
            { "argonianfemalehead_msn.dds", Dest_HeadNormal },
            { "argonianfemalehead_s.dds", Dest_TorsoFemaleSpecular },
            { "ArgonianFemaleHeadOld.dds", Dest_HeadDetail },
            { "argonianfemalebody.dds", Dest_TorsoFemaleDiffuse },
            { "argonianfemalebody_msn.dds", Dest_TorsoFemaleNormal },
            { "argonianfemalebody_s.dds", Dest_TorsoFemaleSpecular },
            { "argonianfemalehands.dds", Dest_HandsFemaleDiffuse },
            { "argonianfemalehands_msn.dds", Dest_HandsFemaleNormal },
            { "argonianfemalehands_s.dds", Dest_HandsFemaleSpecular },

            // Astrid
            {"AstridHead.dds", Dest_HeadDiffuse },
            {"AstridHead_msn.dds", Dest_HeadNormal },
            {"AstridHead_s.dds", Dest_HeadSpecular },
            {"AstridBody.dds", Dest_TorsoFemaleDiffuse },
            {"AstridBody_msn.dds", Dest_TorsoFemaleNormal },
            {"AstridBody_s.dds", Dest_TorsoFemaleSpecular },
            {"AstridHands.dds", Dest_HandsFemaleDiffuse },
            {"AstridHands_msn.dds", Dest_HandsFemaleNormal },
            {"AstridHands_s.dds", Dest_HandsFemaleSpecular },

            // Female Etc
            { Source_EtcFemaleDiffuse, Dest_EtcFemaleDiffusePrimary },
            { Source_EtcFemaleNormal, Dest_EtcFemaleNormalPrimary },
            { Source_EtcFemaleSubsurface, Dest_EtcFemaleSubsurfacePrimary },
            { Source_EtcFemaleSpecular, Dest_EtcFemaleSpecularPrimary },

            // Male TNG
            { Source_TNGMaleDiffuse, Dest_TNGMaleDiffuse },
            { Source_TNGMaleNormal, Dest_TNGMaleNormal },
            { Source_TNGMaleSubsurface, Dest_TNGMaleSubsurface },
            { Source_TNGMaleSpecular, Dest_TNGMaleSpecular },

            { Source_TNGMaleDiffuseAfflicted, Dest_TNGMaleDiffuse },
            { Source_TNGMaleDiffuseArgonian, Dest_TNGMaleDiffuse },
            { Source_TNGMaleDiffuseKhajiit, Dest_TNGMaleDiffuse },
            { Source_TNGMaleDiffuseSnowElf, Dest_TNGMaleDiffuse },
            { Source_TNGMaleNormalArgonian, Dest_TNGMaleNormal },
            { Source_TNGMaleNormalKhajiit, Dest_TNGMaleNormal },
            { Source_TNGMaleNormalElder, Dest_TNGMaleNormal },
            { Source_TNGMaleSpecularArgonian, Dest_TNGMaleSpecular },
            { Source_TNGMaleSpecularKhajiit, Dest_TNGMaleSpecular }
        };

        public static bool HasTNGPaths(IEnumerable<string> paths)
        {
            return paths.Where(x =>
                x.EndsWith(Source_TNGMaleDiffuse, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleNormal, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleSubsurface, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleSpecular, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleDiffuseAfflicted, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleDiffuseArgonian, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleDiffuseKhajiit, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleDiffuseSnowElf, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleNormalArgonian, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleNormalKhajiit, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleNormalElder, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleSpecularArgonian, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_TNGMaleSpecularKhajiit, StringComparison.OrdinalIgnoreCase))
                .Any();
        }

        public static bool HasEtcPaths(IEnumerable<string> paths)
        {
            return paths.Where(x =>
                x.EndsWith(Source_EtcFemaleDiffuse, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_EtcFemaleNormal, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_EtcFemaleSubsurface, StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(Source_EtcFemaleSpecular, StringComparison.OrdinalIgnoreCase))
                .Any();
        }

        public static readonly Dictionary<string, string> MaleTorsoPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            { "malebody_1.dds", Dest_TorsoMaleDiffuse },
            { "maleBody_1_msn.dds", Dest_TorsoMaleNormal },
            { "malebody_1_sk.dds", Dest_TorsoMaleSubsurface },
            { "malebody_1_s.dds", Dest_TorsoMaleSpecular },
            { "malebodyafflicted.dds", Dest_TorsoMaleDiffuse },
            { "bodymale.dds", Dest_TorsoMaleDiffuse },
            { "bodymale_msn.dds", Dest_TorsoMaleNormal },
            { "bodymale_s.dds", Dest_TorsoMaleSpecular },
            { "argonianmalebody.dds", Dest_TorsoMaleDiffuse },
            { "argonianmalebody_msn.dds", Dest_TorsoMaleNormal },
            { "argonianmalebody_s.dds", Dest_TorsoMaleSpecular },
        };

        public static readonly Dictionary<string, string> FemaleTorsoPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            { "femalebody_1.dds",Dest_TorsoFemaleDiffuse },
            { "femaleBody_1_msn.dds", Dest_TorsoFemaleNormal },
            { "femalebody_1_sk.dds", Dest_TorsoFemaleSubsurface },
            { "femalebody_1_s.dds", Dest_TorsoFemaleSpecular },
            { "femalebodyafflicted.dds", Dest_TorsoFemaleDiffuse },
            { "femalebody.dds", Dest_TorsoFemaleDiffuse },
            { "femalebody_msn.dds", Dest_TorsoFemaleNormal },
            { "femalebody_s.dds", Dest_TorsoFemaleSpecular },
            { "argonianfemalebody.dds", Dest_TorsoFemaleDiffuse },
            { "argonianfemalebody_msn.dds", Dest_TorsoFemaleNormal },
            { "argonianfemalebody_s.dds", Dest_TorsoFemaleSpecular },
        };
    }
}
