using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;

namespace SynthEBD;

public class DefaultAttributeGroups
{
    public static AttributeGroup CannotHaveDefinition = new()
    {
        Label = "Cannot Have Definition",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.ServicesMarkarthCastleCook.FormKey,
                            Skyrim.Faction.JobRentRoomFaction.FormKey
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Bard.FormKey,
                            Skyrim.Class.Beggar.FormKey,
                            Skyrim.Class.VendorTailor.FormKey,
                            Skyrim.Class.VendorApothecary.FormKey,
                            Skyrim.Class.VendorPawnbroker.FormKey,
                            Skyrim.Class.VendorSpells.FormKey,
                            Skyrim.Class.VendorFood.FormKey,
                            Skyrim.Class.Citizen.FormKey,
                            Skyrim.Class.Priest.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup MustBeFit = new()
    {
        Label = "Must be Fit",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.CombatAssassin.FormKey,
                            Skyrim.Class.CombatMonk.FormKey,
                            Skyrim.Class.CombatMystic.FormKey,
                            Skyrim.Class.CombatNightblade.FormKey,
                            Skyrim.Class.CombatRogue.FormKey,
                            Skyrim.Class.CombatScout.FormKey,
                            Skyrim.Class.CombatSpellsword.FormKey,
                            Skyrim.Class.CombatThief.FormKey,
                            Skyrim.Class.CombatWarrior1H.FormKey,
                            Skyrim.Class.CombatWarrior2H.FormKey,
                            Skyrim.Class.EncClassAlikrMelee.FormKey,
                            Skyrim.Class.EncClassAlikrMissile.FormKey,
                            Skyrim.Class.EncClassAlikrWizard.FormKey,
                            Skyrim.Class.CWSoldierClass.FormKey,
                            Skyrim.Class.EncClassThalmorMelee.FormKey,
                            Skyrim.Class.EncClassThalmorMissile.FormKey,
                            Skyrim.Class.EncClassThalmorWizard.FormKey,
                            Skyrim.Class.Miner.FormKey,
                            Skyrim.Class.SoldierImperialNotGuard.FormKey,
                            Skyrim.Class.SoldierSonsSkyrimNotGuard.FormKey,
                            Skyrim.Class.TrainerLightArmorJourneyman.FormKey,
                            Skyrim.Class.TrainerLockpickExpert.FormKey,
                            Skyrim.Class.TrainerMarksmanJourneyman.FormKey,
                            Skyrim.Class.TrainerMarksmanExpert.FormKey,
                            Skyrim.Class.TrainerOneHandedJourneyman.FormKey,
                            Skyrim.Class.TrainerPickpocketExpert.FormKey,
                            Skyrim.Class.TrainerSmithingJourneyman.FormKey,
                            Skyrim.Class.TrainerSneakJourneyman.FormKey,
                            Skyrim.Class.TrainerSneakExpert.FormKey,
                            Skyrim.Class.VendorFletcher.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup MustBeAthletic = new()
    {
        Label = "Must be Athletic",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.CombatAssassin.FormKey,
                            Skyrim.Class.CombatNightingale.FormKey,
                            Skyrim.Class.CombatRanger.FormKey,
                            Skyrim.Class.CombatRogue.FormKey,
                            Skyrim.Class.EncClassThalmorMelee.FormKey,
                            Skyrim.Class.GuardOrc1H.FormKey,
                            Skyrim.Class.GuardOrc2H.FormKey,
                            Skyrim.Class.Lumberjack.FormKey,
                            Skyrim.Class.Miner.FormKey,
                            Skyrim.Class.SoldierImperialNotGuard.FormKey,
                            Skyrim.Class.SoldierSonsSkyrimNotGuard.FormKey,
                            Skyrim.Class.TrainerBlockExpert.FormKey,
                            Skyrim.Class.TrainerHeavyArmorExpert.FormKey,
                            Skyrim.Class.TrainerLightArmorExpert.FormKey,
                            Skyrim.Class.TrainerLockpickMaster.FormKey,
                            Skyrim.Class.TrainerMarksmanMaster.FormKey,
                            Skyrim.Class.TrainerOneHandedExpert.FormKey,
                            Skyrim.Class.TrainerPickpocketMaster.FormKey,
                            Skyrim.Class.TrainerSmithingExpert.FormKey,
                            Skyrim.Class.TrainerSneakMaster.FormKey,
                            Skyrim.Class.TrainerTwoHandedExpert.FormKey,
                            Skyrim.Class.Vigilant1hMeleeClass.FormKey,
                            Skyrim.Class.Vigilant2hMeleeClass.FormKey,
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.PenitusOculatusFaction.FormKey,
                            Skyrim.Faction.CompanionsCircle.FormKey,
                            Dawnguard.Faction.DLC1DawnguardExteriorGuardFaction.FormKey,
                            Dawnguard.Faction.DLC1HunterFaction.FormKey,
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup MustBeMuscular = new()
    {
        Label = "Must be Muscular",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Blade.FormKey,
                            Skyrim.Class.EncClassPenitusOculatus.FormKey,
                            Skyrim.Class.GuardOrc1H.FormKey,
                            Skyrim.Class.GuardOrc2H.FormKey,
                            Skyrim.Class.Lumberjack.FormKey,
                            Skyrim.Class.TrainerBlockMaster.FormKey,
                            Skyrim.Class.TrainerHeavyArmorMaster.FormKey,
                            Skyrim.Class.TrainerLightArmorMaster.FormKey,
                            Skyrim.Class.TrainerOneHandedMaster.FormKey,
                            Skyrim.Class.TrainerSmithingMaster.FormKey,
                            Skyrim.Class.TrainerTwoHandedMaster.FormKey,
                            Skyrim.Class.VendorBlacksmith.FormKey,
                            Skyrim.Class.Vigilant1hMeleeClass.FormKey,
                            Skyrim.Class.Vigilant2hMeleeClass.FormKey,
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeNPC()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Npc.AelaTheHuntress.FormKey,
                            Skyrim.Npc.Farkas.FormKey,
                            Skyrim.Npc.HirelingBelrand.FormKey,
                            Skyrim.Npc.Borgakh.FormKey,
                            Skyrim.Npc.Borkul.FormKey,
                            Skyrim.Npc.Ulfric.FormKey,
                            Skyrim.Npc.Galmar.FormKey,
                            Skyrim.Npc.Mjoll.FormKey,
                            Skyrim.Npc.Uthgerd.FormKey,
                            Skyrim.Npc.GormlaithGoldenHilt.FormKey,
                            Dawnguard.Npc.DLC1Isran.FormKey,
                            Dragonborn.Npc.DLC2EbonyWarrior.FormKey,
                            Dragonborn.Npc.DLC2Frea.FormKey,
                            Dragonborn.Npc.DLC2Miraak.FormKey
                        },
                        Weighting = 10
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.PenitusOculatusFaction.FormKey,
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup CanGetChubbyMorph = new()
    {
        Label = "Can Get Chubby Morph",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.EncClassBanditMissile.FormKey,
                            Skyrim.Class.EncClassBanditWizard.FormKey,
                            Skyrim.Class.VendorTailor.FormKey,
                            Skyrim.Class.VendorApothecary.FormKey,
                            Skyrim.Class.VendorPawnbroker.FormKey,
                            Skyrim.Class.VendorSpells.FormKey,
                            Skyrim.Class.VendorFood.FormKey,
                            Skyrim.Class.GuardImperial.FormKey
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Citizen.FormKey
                        }
                    },
                    new NPCAttributeMisc()
                    {
                        Unique = ThreeWayState.IsNot
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.ServicesMarkarthCastleCook.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup CannotHaveScars = new()
    {
        Label = "Cannot Have Scars",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Bard.FormKey,
                            Skyrim.Class.VendorTailor.FormKey,
                            Skyrim.Class.VendorApothecary.FormKey,
                            Skyrim.Class.VendorPawnbroker.FormKey,
                            Skyrim.Class.VendorSpells.FormKey,
                            Skyrim.Class.VendorFood.FormKey,
                            Skyrim.Class.Citizen.FormKey,
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.ServicesMarkarthCastleCook.FormKey,
                            Skyrim.Faction.WERoad02NobleFaction.FormKey,
                            Skyrim.Faction.JobStewardFaction.FormKey,
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup CanBeDirty = new()
    {
        Label = "Can Be Dirty",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.EncClassBanditMelee.FormKey,
                            Skyrim.Class.EncClassBanditMissile.FormKey,
                            Skyrim.Class.EncClassBanditWizard.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup MustBeDirty = new()
    {
        Label = "Must Be Dirty",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Beggar.FormKey,
                            Skyrim.Class.Miner.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup MustGetYoungFace = new()
    {
        Label = "Must Get Young Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeVoiceType()
                    {
                        FormKeys = new()
                        {
                            Skyrim.VoiceType.MaleYoungEager.FormKey,
                            Skyrim.VoiceType.FemaleYoungEager.FormKey
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeNPC()
                    {
                        FormKeys = new()
                        {
                            FormKey.Factory("052FE7:3DNPC.esp") // Hjoromir
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Bard.FormKey
                        }
                    },
                    new NPCAttributeNPC()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Npc.TalsgarTheWanderer.FormKey
                        },
                        Not = true
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.JobBardFaction.FormKey,
                            Skyrim.Faction.BardSingerFaction.FormKey,
                            Skyrim.Faction.JobInnServer.FormKey
                        }
                    },
                    new NPCAttributeNPC()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Npc.TalsgarTheWanderer.FormKey
                        },
                        Not = true
                    }
                }
            }
        }
    };

    public static AttributeGroup MatureFace = new()
    {
        Label = "Can Get Mildy Older Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.JobInnkeeperFaction.FormKey
                        }
                    },
                    new NPCAttributeFactions()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Faction.JobInnServer.FormKey
                        },
                        Not = true
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeVoiceType()
                    {
                        FormKeys = new()
                        {
                            Skyrim.VoiceType.FemaleCommander.FormKey,
                            Skyrim.VoiceType.MaleCommander.FormKey,
                            Skyrim.VoiceType.MaleNordCommander.FormKey,
                            Skyrim.VoiceType.MaleDrunk.FormKey,
                            Skyrim.VoiceType.MaleForsworn.FormKey,
                            Skyrim.VoiceType.MaleSoldier.FormKey,
                            Skyrim.VoiceType.MaleBrute.FormKey,
                            Skyrim.VoiceType.FemaleSoldier.FormKey
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeMisc()
                    {
                        Unique = ThreeWayState.IsNot
                    }
                }
            }
        }
    };

    public static AttributeGroup HaggardFace = new()
    {
        Label = "Can Get Haggard Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeClass()
                    {
                        FormKeys = new()
                        {
                            Skyrim.Class.Beggar.FormKey,
                            Skyrim.Class.EncClassBanditMelee.FormKey,
                            Skyrim.Class.EncClassBanditMissile.FormKey,
                            Skyrim.Class.EncClassBanditWizard.FormKey,
                            Skyrim.Class.Miner.FormKey
                        }
                    }
                }
            },
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeVoiceType()
                    {
                        FormKeys = new()
                        {
                            Skyrim.VoiceType.MaleDrunk.FormKey,
                            Skyrim.VoiceType.FemaleCommander.FormKey,
                            Skyrim.VoiceType.MaleCommander.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Age40 = new()
    {
        Label = "Must Get Age40 Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadFemaleBretonComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleDarkElfComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleHighElfComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleImperialComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleNordComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleOrcComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleRedguardComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleWoodElfComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleBretonComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleDarkElfComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleHighElfComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleImperialComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleNordComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleOrcComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleRedGuardComplexion_Age40.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleWoodElfComplexion_Age40.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Age40Rough = new()
    {
        Label = "Must Get Age40 Rough Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadMaleBretonComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleDarkElfComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleHighElfComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleImperialComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleNordComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleOrcComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleRedGuardComplexion_Age40Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleWoodElfComplexion_Age40Rough.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Age50 = new()
    {
        Label = "Must Get Age50 Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadFemaleBretonComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleDarkElfComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleHighElfComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleImperialComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleNordComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleOrcComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleRedguardComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleWoodElfComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleBretonComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleDarkElfComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleHighElfComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleImperialComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleNordComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleOrcComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleRedGuardComplexion_Age50.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleWoodElfComplexion_Age50.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Freckles = new()
    {
        Label = "Must Get Face Freckles",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadFemaleBretonComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleDarkElfComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleHighElfComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleImperialComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleNordComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleOrcComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleRedguardComplexion_Frekles.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleWoodElfComplexion_Frekles.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Rough01 = new()
    {
        Label = "Must Get Rough01 Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadFemaleBretonComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleDarkElfComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleHighElfComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleImperialComplexion_Rough.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleNordRough.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleOrcComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleRedguardComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadFemaleWoodElfComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleBretonComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleDarkElfComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleHighElfComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleImperialComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleNordComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleOrcComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleRedGuardComplexion_Rough01.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleWoodElfComplexion_Rough01.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup Rough02 = new()
    {
        Label = "Must Get Rough02 Face",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeFaceTexture()
                    {
                        FormKeys = new()
                        {
                            Skyrim.TextureSet.SkinHeadMaleBretonComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleDarkElfComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleHighElfComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleImperialComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleNordComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleOrcComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleRedGuardComplexion_Rough02.FormKey,
                            Skyrim.TextureSet.SkinHeadMaleWoodElfComplexion_Rough02.FormKey
                        }
                    }
                }
            }
        }
    };

    public static AttributeGroup CharmersOfTheReachHeads = new()
    {
        Label = "Has CotR Head",
        Attributes = new()
        {
            new NPCAttribute()
            {
                SubAttributes = new()
                {
                    new NPCAttributeMod()
                    {
                        ModKeys = new()
                        {
                            ModKey.FromNameAndExtension("Refined Volkihars.esp"),
                            ModKey.FromNameAndExtension("Refined Auri.esp"),
                            ModKey.FromNameAndExtension("BjornRefined.esp"),
                            ModKey.FromNameAndExtension("KaidanRefined.esp"),
                            ModKey.FromNameAndExtension("GorrRefined.esp"),
                            ModKey.FromNameAndExtension("SofiaRefined.esp"),
                            ModKey.FromNameAndExtension("GeneralTulliusRefined.esp"),
                            ModKey.FromNameAndExtension("MarcurioRefined.esp"),
                            ModKey.FromNameAndExtension("MarcusTheKnightRefined.esp"),
                            ModKey.FromNameAndExtension("Vigilant NPCs Refined.esp"),
                            ModKey.FromNameAndExtension("Glenmoril NPCs Refined.esp"),
                            ModKey.FromNameAndExtension("KW_UnslaadNPCsRefined.esp"),
                            ModKey.FromNameAndExtension("MOSRefined.esp"),
                            ModKey.FromNameAndExtension("TSOSRefined.esp"),
                            ModKey.FromNameAndExtension("TSOSRefinedDawnguard.esp"),
                            ModKey.FromNameAndExtension("TSOSRefinedDragonborn.esp"),
                            ModKey.FromNameAndExtension("TSOSRefinedHearthfire.esp"),
                            ModKey.FromNameAndExtension("TSOSRefinedStenvar.esp"),
                            ModKey.FromNameAndExtension("TSOSMFMiraak.esp"),
                            ModKey.FromNameAndExtension("0TSOSRefinedDwarfofSkyrim.esp"),
                            ModKey.FromNameAndExtension("TSOS_Apachii_Store_Patch.esp"),
                            ModKey.FromNameAndExtension("TSOS_Aesir_Weaponry_Patch.esp"),
                            ModKey.FromNameAndExtension("TDOSRefined.esp"),
                            ModKey.FromNameAndExtension("Karura's Companions Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Dark Brotherhood Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Cicero Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Brynjolf Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Thieves Guild Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Daedric Princes Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Ordinary People Refined.esp"),
                            ModKey.FromNameAndExtension("Karura's Ordinary People Refined BUVARP Patch.esp"),
                            ModKey.FromNameAndExtension("Mythos Followers Refined.esp"),
                            ModKey.FromNameAndExtension("Mythos Followers Refined - ezPG.esp")
                        }
                    }
                }
            }
        }
    };
}