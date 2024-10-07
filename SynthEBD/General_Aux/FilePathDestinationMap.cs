using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
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
        public const string Dest_HeadDiffuse = "HeadTexture.Diffuse.GivenPath";
        public const string Dest_HeadNormal = "HeadTexture.NormalOrGloss.GivenPath";
        public const string Dest_HeadSubsurface = "HeadTexture.GlowOrDetailMap.GivenPath";
        public const string Dest_HeadSpecular = "HeadTexture.BacklightMaskOrSpecular.GivenPath";
        public const string Dest_HeadDetail = "HeadTexture.Height.GivenPath";

        // male torso
        public const string Dest_TorsoMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
        public const string Dest_TorsoMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath";
        public const string Dest_TorsoMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath";
        public const string Dest_TorsoMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath";

        // female torso
        public const string Dest_TorsoFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";
        public const string Dest_TorsoFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath";
        public const string Dest_TorsoFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath";
        public const string Dest_TorsoFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath";

        // male handsTail
        public const string Dest_HandsMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
        public const string Dest_HandsMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath";
        public const string Dest_HandsMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath";
        public const string Dest_HandsMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath";

        // female hands
        public const string Dest_HandsFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";
        public const string Dest_HandsFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath";
        public const string Dest_HandsFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath";
        public const string Dest_HandsFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Hands) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath";

        // male feet
        public const string Dest_FeetMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
        public const string Dest_FeetMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath";
        public const string Dest_FeetMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath";
        public const string Dest_FeetMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath";

        // female feet
        public const string Dest_FeetFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";
        public const string Dest_FeetFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath";
        public const string Dest_FeetFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath";
        public const string Dest_FeetFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Feet) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath";

        // male tail
        public const string Dest_TailMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
        public const string Dest_TailMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath";
        public const string Dest_TailMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath";
        public const string Dest_TailMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath";

        // female tail
        public const string Dest_TailFemaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.Diffuse.GivenPath";
        public const string Dest_TailFemaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.NormalOrGloss.GivenPath";
        public const string Dest_TailFemaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.GlowOrDetailMap.GivenPath";
        public const string Dest_TailFemaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Tail) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Female.BacklightMaskOrSpecular.GivenPath";

        // male TNG 
        public const string Dest_TNGMaleDiffuse = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.Diffuse.GivenPath";
        public const string Dest_TNGMaleNormal = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.NormalOrGloss.GivenPath";
        public const string Dest_TNGMaleSubsurface = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.GlowOrDetailMap.GivenPath";
        public const string Dest_TNGMaleSpecular = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag((BipedObjectFlag)4194304) && MatchRace(Race, AdditionalRaces, MatchDefault)].SkinTexture.Male.BacklightMaskOrSpecular.GivenPath";

        // female etc
        public const string Dest_EtcFemaleDiffusePrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.Diffuse.GivenPath";
        public const string Dest_EtcFemaleDiffuseSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.Diffuse.GivenPath";
        public const string Dest_EtcFemaleNormalPrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.NormalOrGloss.GivenPath";
        public const string Dest_EtcFemaleNormalSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.NormalOrGloss.GivenPath";
        public const string Dest_EtcFemaleSubsurfacePrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.GlowOrDetailMap.GivenPath";
        public const string Dest_EtcFemaleSubsurfaceSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.GlowOrDetailMap.GivenPath";
        public const string Dest_EtcFemaleSpecularPrimary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 1].NewTexture.BacklightMaskOrSpecular.GivenPath";
        public const string Dest_EtcFemaleSpecularSecondary = "WornArmor.Armature[BodyTemplate.FirstPersonFlags.Invoke:HasFlag(BipedObjectFlag.Body) && MatchRace(Race, AdditionalRaces, MatchDefault)].WorldModel.Female.AlternateTextures[Index == 2].NewTexture.BacklightMaskOrSpecular.GivenPath";

        /// <summary>
        /// Source File Names
        /// </summary>
        /// 

        public const string Source_HeadDetailDefault = "blankdetailmap.dds";

        public const string Source_HeadDiffuseMale = "malehead.dds";
        public const string Source_HeadNormalMale = "malehead_msn.dds";
        public const string Source_HeadSubsurfaceMale = "malehead_sk.dds";
        public const string Source_HeadSpecularMale = "malehead_s.dds";

        public const string Source_HeadDetailAge40Male = "maleheaddetail_age40.dds";
        public const string Source_HeadDetailAge40RoughMale = "maleheaddetail_age40rough.dds";
        public const string Source_HeadDetailAge50Male = "maleheaddetail_age50.dds";
        public const string Source_HeadDetailRough01Male = "maleheaddetail_rough01.dds";
        public const string Source_HeadDetailRough02Male = "maleheaddetail_rough02.dds";

        public const string Source_TorsoDiffuseMale = "malebody_1.dds";
        public const string Source_TorsoNormalMale = "maleBody_1_msn.dds";
        public const string Source_TorsoSubsurfaceMale = "malebody_1_sk.dds";
        public const string Source_TorsoSpecularMale = "malebody_1_s.dds";

        public const string Source_HandsDiffuseMale = "malehands_1.dds";
        public const string Source_HandsNormalMale = "malehands_1_msn.dds";
        public const string Source_HandsSubsurfaceMale = "malehands_1_sk.dds";
        public const string Source_HandsSpecularMale = "malehands_1_s.dds";

        public const string Source_FeetDiffuseMale = "malebody_1_feet.dds";
        public const string Source_FeetNormalMale = "malebody_1_msn_feet.dds";
        public const string Source_FeetSubsurfaceMale = "malebody_1_feet_sk.dds";
        public const string Source_FeetSpecularMale = "malebody_1_feet_s.dds";

        public const string Source_HeadDiffuseVampireMale = "maleheadvampire.dds";
        public const string Source_HeadNormalVampireMale = "maleheadvampire_msn.dds";
        public const string Source_HeadNormalOrcMale = "maleheadorc_msn.dds";

        public const string Source_HeadDiffuseAfflictedMale = "maleheadafflicted.dds";
        public const string Source_TorsoDiffuseAfflictedMale = "malebodyafflicted.dds";
        public const string Source_HandsDiffuseAfflictedMale = "malehandsafflicted.dds";
        public const string Source_FeetDiffuseAfflictedMale = "malebodyafflicted_feet.dds";

        public const string Source_HeadDiffuseSnowElfMale = "maleheadsnowelf.dds";
        public const string Source_BodyDiffuseSnowElfMale = "malebodysnowelf.dds";
        public const string Source_HandsDiffuseSnowElfMale = "malehandssnowelf.dds";
        public const string Source_FeetDiffuseSnowElfMale = "malebodysnowelf_feet.dds";

        public const string Source_HeadDiffuseKhajiitMale = "KhajiitMaleHead.dds";
        public const string Source_HeadNormalKhajiitMale = "KhajiitMaleHead_msn.dds";
        public const string Source_HeadSpecularKhajiitMale = "khajiitmalehead_s.dds";
        public const string Source_HeadDetailKhajiitOldMale = "KhajiitOld.dds";
        public const string Source_TorsoDiffuseKhajiitMale = "bodymale.dds";
        public const string Source_TorsoNormalKhajiitMale = "bodymale_msn.dds";
        public const string Source_TorsoSubsurfaceKhajiitMale = "bodymale_sk.dds";
        public const string Source_TorsoSpecularKhajiitMale = "bodymale_s.dds";
        public const string Source_HandsDiffuseKhajiitMale = "HandsMale.dds";
        public const string Source_HandsNormalKhajiitMale = "HandsMale_msn.dds";
        public const string Source_HandsSpecularKhajiitMale = "handsmale_s.dds";
        public const string Source_FeetDiffuseKhajiitMale = "bodymale_feet.dds";
        public const string Source_FeetSubsurfaceKhajiitMale = "bodymale_feet_sk.dds";
        public const string Source_FeetSpecularKhajiitMale = "bodymale_feet_s.dds";

        public const string Source_HeadDiffuseArgonianMale = "ArgonianMaleHead.dds";
        public const string Source_HeadNormalArgonianMale = "ArgonianMaleHead_msn.dds";
        public const string Source_HeadSpecularArgonianMale = "ArgonianMaleHead_s.dds";
        public const string Source_HeadDetailArgonianOldMale = "ArgonianMaleHeadOld.dds";
        public const string Source_TorsoDiffuseArgonianMale = "argonianmalebody.dds";
        public const string Source_TorsoNormalArgonianMale = "argonianmalebody_msn.dds";
        public const string Source_TorsoSubsurfaceArgonianMale = "argonianmalebody_sk.dds";
        public const string Source_TorsoSpecularArgonianMale = "argonianmalebody_s.dds";
        public const string Source_HandsDiffuseArgonianMale = "ArgonianMaleHands.dds";
        public const string Source_HandsNormalArgonianMale = "ArgonianMaleHands_msn.dds";
        public const string Source_HandsSpecularArgonianMale = "ArgonianMaleHands_s.dds";
        public const string Source_FeetDiffuseArgonianMale = "argonianmalebody_feet.dds";
        public const string Source_FeetSubsurfaceArgonianMale = "argonianmalebody_feet_sk.dds";
        public const string Source_FeetSpecularArgonianMale = "argonianmalebody_feet_s.dds";

        public const string Source_TNGMaleDiffuse = "malegenitals_1.dds";
        public const string Source_TNGMaleNormal = "malegenitals_1_msn.dds";
        public const string Source_TNGMaleSubsurface = "malegenitals_1_sk.dds";
        public const string Source_TNGMaleSpecular = "malegenitals_1_s.dds";

        public const string Source_HeadDiffuseFemaleAndKhajiitF = "femalehead.dds"; 
        public const string Source_HeadNormalFemaleAndKhajiitF = "femalehead_msn.dds";
        public const string Source_HeadSubsurfaceFemaleAndKhajiitF = "femalehead_sk.dds";
        public const string Source_HeadSpecularFemaleAndKhajiitF = "femalehead_s.dds";

        public const string Source_HeadDetailAge40Female = "femaleheaddetail_age40.dds";
        public const string Source_HeadDetailAge40RoughFemale = "femaleheaddetail_age40rough.dds";
        public const string Source_HeadDetailAge50Female = "femaleheaddetail_age50.dds";
        public const string Source_HeadDetailRoughFemale = "femaleheaddetail_rough.dds";
        public const string Source_HeadDetailFrecklesFemale = "femaleheaddetail_frekles.dds";

        public const string Source_TorsoDiffuseFemale = "femalebody_1.dds";
        public const string Source_TorsoNormalFemale = "femaleBody_1_msn.dds";
        public const string Source_TorsoSubsurfaceFemale = "femalebody_1_sk.dds";
        public const string Source_TorsoSpecularFemale = "femalebody_1_s.dds";

        public const string Source_HandsDiffuseFemale = "femalehands_1.dds";
        public const string Source_HandsNormalFemale = "femalehands_1_msn.dds";
        public const string Source_HandsSubsurfaceFemale = "femalehands_1_sk.dds";
        public const string Source_HandsSpecularFemale = "femalehands_1_s.dds";

        public const string Source_FeetDiffuseFemale = "femalebody_1_feet.dds";
        public const string Source_FeetNormalFemale = "femalebody_1_msn_feet.dds";
        public const string Source_FeetSubsurfaceFemale = "femalebody_1_feet_sk.dds";
        public const string Source_FeetSpecularFemale = "femalebody_1_feet_s.dds";

        public const string Source_HeadDiffuseVampireFemale = "femaleheadvampire.dds";
        public const string Source_HeadNormalVampireFemale = "femaleheadvampire_msn.dds";
        public const string Source_HeadNormalOrcFemale = "femaleheadorc_msn.dds";
        public const string Source_HeadSubsurfaceVampireFemale = "femaleheadvampire_sk.dds";
        public const string Source_HeadSpecularVampireFemale = "femaleheadvampire_s.dds";

        public const string Source_HeadDiffuseAfflictedFemale = "femaleheadafflicted.dds";
        public const string Source_TorsoDiffuseAfflictedFemale = "femalebodyafflicted.dds";
        public const string Source_HandsDiffuseAfflictedFemale = "femalehandsafflicted.dds";
        public const string Source_FeetDiffuseAfflictedFemale = "femalebodyafflicted_feet.dds";

        public const string Source_TorsoDiffuseKhajiitFemale = "femalebody.dds";
        public const string Source_TorsoNormalKhajiitFemale = "femalebody_msn.dds";
        public const string Source_TorsoSpecularKhajiitFemale = "femalebody_s.dds";
        public const string Source_HandsDiffuseKhajiitFemale = "femalehands.dds";
        public const string Source_HandsNormalKhajiitFemale = "femalehands_msn.dds";
        public const string Source_HandsSpecularKhajiitFemale = "femalehands_s.dds";

        public const string Source_HeadDiffuseArgonianFemale = "argonianfemalehead.dds";
        public const string Source_HeadNormalArgonianFemale = "argonianfemalehead_msn.dds";
        public const string Source_HeadSpecularArgonianFemale = "argonianfemalehead_s.dds";
        public const string Source_HeadDetailArgonianOldFemale = "ArgonianFemaleHeadOld.dds";
        public const string Source_TorsoDiffuseArgonianFemale = "argonianfemalebody.dds";
        public const string Source_TorsoNormalArgonianFemale = "argonianfemalebody_msn.dds";
        public const string Source_TorsoSpecularArgonianFemale = "argonianfemalebody_s.dds";
        public const string Source_HandsDiffuseArgonianFemale = "argonianfemalehands.dds";
        public const string Source_HandsNormalArgonianFemale = "argonianfemalehands_msn.dds";
        public const string Source_HandsSpecularArgonianFemale = "argonianfemalehands_s.dds";

        public const string Source_HeadDiffuseAstrid = "AstridHead.dds";
        public const string Source_HeadNormalAstrid = "AstridHead_msn.dds";
        public const string Source_HeadSpecularAstrid = "AstridHead_s.dds";
        public const string Source_BodyDiffuseAstrid = "AstridBody.dds";
        public const string Source_BodyNormalAstrid = "AstridBody_msn.dds";
        public const string Source_BodySpecularAstrid = "AstridBody_s.dds";
        public const string Source_HandsDiffuseAstrid = "AstridHands.dds";
        public const string Source_HandsNormalAstrid = "AstridHands_msn.dds";
        public const string Source_HandsSpecularAstrid = "AstridHands_s.dds";

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
            { Source_HeadDetailDefault, Dest_HeadDetail }, // common to male and female

            //male head
            { Source_HeadDiffuseMale,  Dest_HeadDiffuse},
            { Source_HeadNormalMale, Dest_HeadNormal },
            { Source_HeadSubsurfaceMale,  Dest_HeadSubsurface},
            { Source_HeadSpecularMale, Dest_HeadSpecular },
            { Source_HeadDetailAge40Male,  Dest_HeadDetail},
            { Source_HeadDetailAge40RoughMale, Dest_HeadDetail },
            { Source_HeadDetailAge50Male, Dest_HeadDetail },
            { Source_HeadDetailRough01Male, Dest_HeadDetail },
            { Source_HeadDetailRough02Male, Dest_HeadDetail },

            //male torso
            { Source_TorsoDiffuseMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalMale, Dest_TorsoMaleNormal },
            { Source_TorsoSubsurfaceMale, Dest_TorsoMaleSubsurface },
            { Source_TorsoSpecularMale, Dest_TorsoMaleSpecular },

            //male hands
            { Source_HandsDiffuseMale, Dest_HandsMaleDiffuse },
            { Source_HandsNormalMale, Dest_HandsMaleNormal },
            { Source_HandsSubsurfaceMale, Dest_HandsMaleSubsurface },
            { Source_HandsSpecularMale, Dest_HandsMaleSpecular },

            //male feet
            { Source_FeetDiffuseMale, Dest_FeetMaleDiffuse },
            { Source_FeetNormalMale, Dest_FeetMaleNormal },
            { Source_FeetSubsurfaceMale, Dest_FeetMaleSubsurface },
            { Source_FeetSpecularMale, Dest_FeetMaleSpecular },

            //male vampire
            { Source_HeadDiffuseVampireMale, Dest_HeadDiffuse },
            { Source_HeadNormalVampireMale, Dest_HeadNormal },
            { Source_HeadNormalOrcMale, Dest_HeadNormal },

            //male afflicted
            { Source_HeadDiffuseAfflictedMale, Dest_HeadDiffuse },
            { Source_TorsoDiffuseAfflictedMale, Dest_TorsoMaleDiffuse },
            { Source_HandsDiffuseAfflictedMale, Dest_HandsMaleDiffuse },

            //male snow elf
            { Source_HeadDiffuseSnowElfMale, Dest_HeadDiffuse },
            { Source_BodyDiffuseSnowElfMale, Dest_TorsoMaleDiffuse },
            { Source_HandsDiffuseSnowElfMale, Dest_HandsMaleDiffuse },

            //male khajiit
            { Source_HeadDiffuseKhajiitMale, Dest_HeadDiffuse },
            { Source_HeadNormalKhajiitMale, Dest_HeadNormal },
            { Source_HeadSpecularKhajiitMale, Dest_HeadSpecular },
            { Source_HeadDetailKhajiitOldMale, Dest_HeadDetail }, //note: used by khajiit female as well
            { Source_TorsoDiffuseKhajiitMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalKhajiitMale, Dest_TorsoMaleNormal },
            { Source_TorsoSpecularKhajiitMale, Dest_TorsoMaleSpecular },
            { Source_HandsDiffuseKhajiitMale, Dest_HandsMaleDiffuse },
            { Source_HandsNormalKhajiitMale, Dest_HandsMaleNormal },
            { Source_HandsSpecularKhajiitMale, Dest_HandsMaleSpecular },

            //male argonian
            { Source_HeadDiffuseArgonianMale, Dest_HeadDiffuse },
            { Source_HeadNormalArgonianMale, Dest_HeadNormal },
            { Source_HeadSpecularArgonianMale, Dest_HeadSpecular },
            { Source_HeadDetailArgonianOldMale, Dest_HeadDetail },
            { Source_TorsoDiffuseArgonianMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalArgonianMale, Dest_TorsoMaleNormal },
            { Source_TorsoSpecularArgonianMale, Dest_TorsoMaleSpecular },
            { Source_HandsDiffuseArgonianMale, Dest_HandsMaleDiffuse },
            { Source_HandsNormalArgonianMale, Dest_HandsMaleNormal },
            { Source_HandsSpecularArgonianMale, Dest_HandsMaleSpecular },

            //female head
            { Source_HeadDiffuseFemaleAndKhajiitF, Dest_HeadDiffuse }, // note: used by khajiit female as well
            { Source_HeadNormalFemaleAndKhajiitF, Dest_HeadNormal }, // note: used by khajiit female as well
            { Source_HeadSubsurfaceFemaleAndKhajiitF, Dest_HeadSubsurface }, // note: used by khajiit female as well
            { Source_HeadSpecularFemaleAndKhajiitF, Dest_HeadSpecular }, // note: used by khajiit female as well
            { Source_HeadDetailAge40Female, Dest_HeadDetail },
            { Source_HeadDetailAge50Female, Dest_HeadDetail },
            { Source_HeadDetailAge40RoughFemale, Dest_HeadDetail },
            { Source_HeadDetailRoughFemale, Dest_HeadDetail },
            { Source_HeadDetailFrecklesFemale, Dest_HeadDetail },

            //female body
            { Source_TorsoDiffuseFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSubsurfaceFemale, Dest_TorsoFemaleSubsurface },
            { Source_TorsoSpecularFemale, Dest_TorsoFemaleSpecular },

            //female hands
            { Source_HandsDiffuseFemale, Dest_HandsFemaleDiffuse },
            { Source_HandsNormalFemale, Dest_HandsFemaleNormal },
            { Source_HandsSubsurfaceFemale, Dest_HandsFemaleSubsurface },
            { Source_HandsSpecularFemale, Dest_HandsFemaleSpecular },

            //female feet
            { Source_FeetDiffuseFemale, Dest_FeetFemaleDiffuse },
            { Source_FeetNormalFemale, Dest_FeetFemaleNormal },
            { Source_FeetSubsurfaceFemale, Dest_FeetFemaleSubsurface },
            { Source_FeetSpecularFemale, Dest_FeetFemaleSpecular },

            //female vampire
            { Source_HeadDiffuseVampireFemale, Dest_HeadDiffuse },
            { Source_HeadNormalVampireFemale, Dest_HeadNormal },
            { Source_HeadNormalOrcFemale, Dest_HeadNormal },
            { Source_HeadSubsurfaceVampireFemale, Dest_HeadSubsurface },
            { Source_HeadSpecularVampireFemale, Dest_HeadSpecular },

            //female afflicted
            { Source_HeadDiffuseAfflictedFemale, Dest_HeadDiffuse },
            { Source_TorsoDiffuseAfflictedFemale, Dest_TorsoFemaleDiffuse },
            { Source_HandsDiffuseAfflictedFemale, Dest_HandsFemaleDiffuse },

            //female khajiit
            { Source_TorsoDiffuseKhajiitFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalKhajiitFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSpecularKhajiitFemale, Dest_TorsoFemaleSpecular },
            { Source_HandsDiffuseKhajiitFemale, Dest_HandsFemaleDiffuse },
            { Source_HandsNormalKhajiitFemale, Dest_HandsFemaleNormal },
            { Source_HandsSpecularKhajiitFemale, Dest_HandsFemaleSpecular },

            //female argonian
            { Source_HeadDiffuseArgonianFemale, Dest_HeadDiffuse },
            { Source_HeadNormalArgonianFemale, Dest_HeadNormal },
            { Source_HeadSpecularArgonianFemale, Dest_TorsoFemaleSpecular },
            { Source_HeadDetailArgonianOldFemale, Dest_HeadDetail },
            { Source_TorsoDiffuseArgonianFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalArgonianFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSpecularArgonianFemale, Dest_TorsoFemaleSpecular },
            { Source_HandsDiffuseArgonianFemale, Dest_HandsFemaleDiffuse },
            { Source_HandsNormalArgonianFemale, Dest_HandsFemaleNormal },
            { Source_HandsSpecularArgonianFemale, Dest_HandsFemaleSpecular },

            // Astrid
            { Source_HeadDiffuseAstrid, Dest_HeadDiffuse },
            { Source_HeadNormalAstrid, Dest_HeadNormal },
            { Source_HeadSpecularAstrid, Dest_HeadSpecular },
            { Source_BodyDiffuseAstrid, Dest_TorsoFemaleDiffuse },
            { Source_BodyNormalAstrid, Dest_TorsoFemaleNormal },
            { Source_BodySpecularAstrid, Dest_TorsoFemaleSpecular },
            { Source_HandsDiffuseAstrid, Dest_HandsFemaleDiffuse },
            { Source_HandsNormalAstrid, Dest_HandsFemaleNormal },
            { Source_HandsSpecularAstrid, Dest_HandsFemaleSpecular },

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
            { Source_TorsoDiffuseMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalMale, Dest_TorsoMaleNormal },
            { Source_TorsoSubsurfaceMale, Dest_TorsoMaleSubsurface },
            { Source_TorsoSpecularMale, Dest_TorsoMaleSpecular },
            { Source_TorsoDiffuseAfflictedMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoDiffuseKhajiitMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalKhajiitMale, Dest_TorsoMaleNormal },
            { Source_TorsoSpecularKhajiitMale, Dest_TorsoMaleSpecular },
            { Source_TorsoDiffuseArgonianMale, Dest_TorsoMaleDiffuse },
            { Source_TorsoNormalArgonianMale, Dest_TorsoMaleNormal },
            { Source_TorsoSpecularArgonianMale, Dest_TorsoMaleSpecular },
        };

        public static readonly Dictionary<string, string> FemaleTorsoPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            { Source_TorsoDiffuseFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSubsurfaceFemale, Dest_TorsoFemaleSubsurface },
            { Source_TorsoSpecularFemale, Dest_TorsoFemaleSpecular },
            { Source_TorsoDiffuseAfflictedFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoDiffuseKhajiitFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalKhajiitFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSpecularKhajiitFemale, Dest_TorsoFemaleSpecular },
            { Source_TorsoDiffuseArgonianFemale, Dest_TorsoFemaleDiffuse },
            { Source_TorsoNormalArgonianFemale, Dest_TorsoFemaleNormal },
            { Source_TorsoSpecularArgonianFemale, Dest_TorsoFemaleSpecular },
        };
    }
}
