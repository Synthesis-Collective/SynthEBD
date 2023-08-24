using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    internal static class FilePathDestinationMap
    {
        /// <summary>
        /// Destination Paths
        /// </summary>
        // head
        public static string Dest_HeadDiffuse = "HeadTexture.Diffuse.RawPath";
        public static string Dest_HeadNormal = "HeadTexture.NormalOrGloss.RawPath";
        public static string Dest_HeadSubsurface = "HeadTexture.GlowOrDetailMap.RawPath";
        public static string Dest_HeadSpecular = "HeadTexture.BacklightMaskOrSpecular.RawPath";
        public static string Dest_HeadDetail = "HeadTexture.Height.RawPath";

        // male torso
        public static string Dest_TorsoMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public static string Dest_TorsoMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public static string Dest_TorsoMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public static string Dest_TorsoMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female torso
        public static string Dest_TorsoFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public static string Dest_TorsoFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public static string Dest_TorsoFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public static string Dest_TorsoFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male hands
        public static string Dest_HandsMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public static string Dest_HandsMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public static string Dest_HandsMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public static string Dest_HandsMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female hands
        public static string Dest_HandsFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public static string Dest_HandsFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public static string Dest_HandsFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public static string Dest_HandsFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

        // male feet
        public static string Dest_FeetMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath";
        public static string Dest_FeetMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath";
        public static string Dest_FeetMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath";
        public static string Dest_FeetMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath";

        // female feet
        public static string Dest_FeetFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath";
        public static string Dest_FeetFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath";
        public static string Dest_FeetFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath";
        public static string Dest_FeetFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath";

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
            { "femaleheadvampire_sk.dds", Dest_TorsoFemaleSubsurface },
            { "femaleheadvampire_s.dds", Dest_TorsoFemaleSpecular },

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
            {"AstridHands_s.dds", Dest_HandsFemaleSpecular }
        };

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
