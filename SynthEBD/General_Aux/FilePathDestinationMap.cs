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
            { "blankdetailmap.dds", "HeadTexture.Height.RawPath" }, // common to male and female

            //male head
            { "malehead.dds", "HeadTexture.Diffuse.RawPath" },
            { "malehead_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "malehead_sk.dds", "HeadTexture.GlowOrDetailMap.RawPath" },
            { "malehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" },
            { "maleheaddetail_age40.dds", "HeadTexture.Height.RawPath" },
            { "maleheaddetail_age40rough.dds", "HeadTexture.Height.RawPath" },
            { "maleheaddetail_age50.dds", "HeadTexture.Height.RawPath" },
            { "maleheaddetail_rough01.dds", "HeadTexture.Height.RawPath" },
            { "maleheaddetail_rough02.dds", "HeadTexture.Height.RawPath" },

            //male torso
            { "malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "maleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath" },
            { "malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },

            //male hands
            { "malehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "malehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "malehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath" },
            { "malehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },

            //male feet
            { "malebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "malebody_1_msn_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "malebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath" },
            { "malebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },

            //male vampire
            { "maleheadvampire.dds", "HeadTexture.Diffuse.RawPath" },
            { "maleheadvampire_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "maleheadorc_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },

            //male afflicted
            { "maleheadafflicted.dds", "HeadTexture.Diffuse.RawPath" },
            { "malebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "malehandsafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },

            //male snow elf
            { "maleheadsnowelf.dds", "HeadTexture.Diffuse.RawPath" },
            { "malebodysnowelf.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "malehandssnowelf.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },

            //male khajiit
            { "KhajiitMaleHead.dds", "HeadTexture.Diffuse.RawPath" },
            { "KhajiitMaleHead_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "khajiitmalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" },
            { "KhajiitOld.dds", "HeadTexture.Height.RawPath" }, //note: used by khajiit female as well
            { "bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },
            { "HandsMale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "HandsMale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "handsmale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },

            //male argonian
            { "ArgonianMaleHead.dds", "HeadTexture.Diffuse.RawPath" },
            { "ArgonianMaleHead_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "ArgonianMaleHead_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" },
            { "ArgonianMaleHeadOld.dds", "HeadTexture.Height.RawPath" },
            { "argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },
            { "ArgonianMaleHands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "ArgonianMaleHands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "ArgonianMaleHands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },

            //female head
            { "femalehead.dds", "HeadTexture.Diffuse.RawPath" }, // note: used by khajiit female as well
            { "femalehead_msn.dds", "HeadTexture.NormalOrGloss.RawPath" }, // note: used by khajiit female as well
            { "femalehead_sk.dds", "HeadTexture.GlowOrDetailMap.RawPath" }, // note: used by khajiit female as well
            { "femalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" }, // note: used by khajiit female as well
            { "femaleheaddetail_age40.dds", "HeadTexture.Height.RawPath" },
            { "femaleheaddetail_age50.dds", "HeadTexture.Height.RawPath" },
            { "femaleheaddetail_rough.dds", "HeadTexture.Height.RawPath" },
            { "femaleheaddetail_age40rough.dds", "HeadTexture.Height.RawPath" },
            { "femaleheaddetail_frekles.dds", "HeadTexture.Height.RawPath" },

            //female body
            { "femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femaleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath" },
            { "femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },

            //female hands
            { "femalehands_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalehands_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalehands_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath" },
            { "femalehands_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },

            //female feet
            { "femalebody_1_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalebody_1_msn_feet.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalebody_1_feet_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath" },
            { "femalebody_1_feet_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },

            //female vampire
            { "femaleheadvampire.dds", "HeadTexture.Diffuse.RawPath" },
            { "femaleheadvampire_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "femaleheadorc_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "femaleheadvampire_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" },
            { "femaleheadvampire_sk.dds", "HeadTexture.GlowOrDetailMap.RawPath" },

            //female afflicted
            { "femaleheadafflicted.dds", "HeadTexture.Diffuse.RawPath" },
            { "femalebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalehandsafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },

            //female khajiit
            { "femalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
            { "femalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },

            //female argonian
            { "argonianfemalehead.dds", "HeadTexture.Diffuse.RawPath" },
            { "argonianfemalehead_msn.dds", "HeadTexture.NormalOrGloss.RawPath" },
            { "argonianfemalehead_s.dds", "HeadTexture.BacklightMaskOrSpecular.RawPath" },
            { "ArgonianFemaleHeadOld.dds", "HeadTexture.Height.RawPath" },
            { "argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
            { "argonianfemalehands.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "argonianfemalehands_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "argonianfemalehands_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
        };

        public static readonly Dictionary<string, string> MaleTorsoPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            { "malebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "maleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "malebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.RawPath" },
            { "malebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },
            { "malebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "bodymale.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "bodymale_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "bodymale_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },
            { "argonianmalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.RawPath" },
            { "argonianmalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.RawPath" },
            { "argonianmalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.RawPath" },
        };

        public static readonly Dictionary<string, string> FemaleTorsoPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            { "femalebody_1.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femaleBody_1_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalebody_1_sk.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.RawPath" },
            { "femalebody_1_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
            { "femalebodyafflicted.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "femalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "femalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
            { "argonianfemalebody.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.RawPath" },
            { "argonianfemalebody_msn.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.RawPath" },
            { "argonianfemalebody_s.dds", "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.RawPath" },
        };
    }
}
