using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    internal static class FilePathDestinationMap
    {
        public static readonly Dictionary<string, string> FileNameToDestMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "blankdetailmap.dds", "HeadTexture.Height" }, // common to male and female

            //male head
            { "malehead.dds", "HeadTexture.Diffuse" },
            { "malehead_msn.dds", "HeadTexture.NormalOrGloss" },
            { "malehead_sk.dds", "HeadTexture.GlowOrDetailMap" },
            { "malehead_s.dds", "HeadTexture.BacklightMaskOrSpecular" },
            { "maleheaddetail_age40.dds", "HeadTexture.Height" },
            { "maleheaddetail_age40rough.dds", "HeadTexture.Height" },
            { "maleheaddetail_age50.dds", "HeadTexture.Height" },
            { "maleheaddetail_rough01.dds", "HeadTexture.Height" },
            { "maleheaddetail_rough02.dds", "HeadTexture.Height" },

            //male torso
            { "malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "maleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap" },
            { "malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //male hands
            { "malehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "malehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "malehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap" },
            { "malehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //male feet
            { "malebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "malebody_1_msn_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "malebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap" },
            { "malebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //male vampire
            { "maleheadvampire.dds", "HeadTexture.Diffuse" },
            { "maleheadvampire_msn.dds", "HeadTexture.NormalOrGloss" },
            { "maleheadorc_msn.dds", "HeadTexture.NormalOrGloss" },

            //male afflicted
            { "maleheadafflicted.dds", "HeadTexture.Diffuse" },
            { "malebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "malehandsafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },

            //male snow elf
            { "maleheadsnowelf.dds", "HeadTexture.Diffuse" },
            { "malebodysnowelf.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "malehandssnowelf.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },

            //male khajiit
            { "KhajiitMaleHead.dds", "HeadTexture.Diffuse" },
            { "KhajiitMaleHead_msn.dds", "HeadTexture.NormalOrGloss" },
            { "khajiitmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular" },
            { "KhajiitOld.dds", "HeadTexture.Height" }, //note: used by khajiit female as well
            { "bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },
            { "HandsMale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "HandsMale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "handsmale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //male argonian
            { "ArgonianMaleHead.dds", "HeadTexture.Diffuse" },
            { "ArgonianMaleHead_msn.dds", "HeadTexture.NormalOrGloss" },
            { "ArgonianMaleHead_s.dds", "HeadTexture.BacklightMaskOrSpecular" },
            { "ArgonianMaleHeadOld.dds", "HeadTexture.Height" },
            { "argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },
            { "ArgonianMaleHands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "ArgonianMaleHands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "ArgonianMaleHands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //female head
            { "femalehead.dds", "HeadTexture.Diffuse" }, // note: used by khajiit female as well
            { "femalehead_msn.dds", "HeadTexture.NormalOrGloss" }, // note: used by khajiit female as well
            { "femalehead_sk.dds", "HeadTexture.GlowOrDetailMap" }, // note: used by khajiit female as well
            { "femalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular" }, // note: used by khajiit female as well
            { "femaleheaddetail_age40.dds", "HeadTexture.Height" },
            { "femaleheaddetail_age50.dds", "HeadTexture.Height" },
            { "femaleheaddetail_rough.dds", "HeadTexture.Height" },
            { "femaleheaddetail_age40rough.dds", "HeadTexture.Height" },
            { "femaleheaddetail_frekles.dds", "HeadTexture.Height" },

            //female body
            { "femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse" },
            { "femaleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss" },
            { "femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap" },
            { "femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular" },

            //female hands
            { "femalehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse" },
            { "femalehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss" },
            { "femalehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap" },
            { "femalehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular" },

            //female feet
            { "femalebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse" },
            { "femalebody_1_msn_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss" },
            { "femalebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap" },
            { "femalebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular" },

            //female vampire
            { "femaleheadvampire.dds", "HeadTexture.Diffuse" },
            { "femaleheadvampire_msn.dds", "HeadTexture.NormalOrGloss" },
            { "femaleheadorc_msn.dds", "HeadTexture.NormalOrGloss" },
            { "femaleheadvampire_s.dds", "HeadTexture.BacklightMaskOrSpecular" },
            { "femaleheadvampire_sk.dds", "HeadTexture.GlowOrDetailMap" },

            //female afflicted
            { "femaleheadafflicted.dds", "HeadTexture.Diffuse" },
            { "femalebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse" },
            { "femalehandsafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse" },

            //female khajiit
            { "femalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "femalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "femalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },
            { "femalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "femalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "femalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },

            //female argonian
            { "argonianfemalehead.dds", "HeadTexture.Diffuse" },
            { "argonianfemalehead_msn.dds", "HeadTexture.NormalOrGloss" },
            { "argonianfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular" },
            { "ArgonianFemaleHeadOld.dds", "HeadTexture.Height" },
            { "argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },
            { "argonianfemalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse" },
            { "argonianfemalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss" },
            { "argonianfemalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular" },
        };
    }
}
