using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD
{
    public class DefaultAttributeGroups
    {
        public static AttributeGroup CannotHaveDefinition = new AttributeGroup()
        {
            Label = "Cannot Have Definition",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeFactions()
                        {
                            Type = NPCAttributeType.Faction,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Faction.ServicesMarkarthCastleCook.FormKey
                            }
                        }
                    }
                },
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Class.Bard.FormKey,
                                Skyrim.Class.Beggar.FormKey,
                                Skyrim.Class.VendorTailor.FormKey,
                                Skyrim.Class.VendorApothecary.FormKey,
                                Skyrim.Class.VendorPawnbroker.FormKey,
                                Skyrim.Class.VendorSpells.FormKey,
                                Skyrim.Class.VendorFood.FormKey,
                                Skyrim.Class.Citizen.FormKey
                            }
                        }
                    }
                }
            }
        };

        public static AttributeGroup MustHaveDefinition = new AttributeGroup()
        {
            Label = "Must Have Definition",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeFactions()
                        {
                            Type = NPCAttributeType.Faction,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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

        public static AttributeGroup MustBeFit = new AttributeGroup()
        {
            Label = "Must be Fit",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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

        public static AttributeGroup MustBeAthletic = new AttributeGroup()
        {
            Label = "Must be Athletic",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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
                }
            }
        };

        public static AttributeGroup MustBeMuscular = new AttributeGroup()
        {
            Label = "Must be Muscular",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeNPC()
                        {
                            Type = NPCAttributeType.NPC,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Npc.AelaTheHuntress.FormKey,
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
                            }
                        }
                    }
                }
            }
        };

        public static AttributeGroup CanGetChubbyMorph = new AttributeGroup()
        {
            Label = "Can Get Chubby Morph",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Class.EncClassBanditMissile.FormKey,
                                Skyrim.Class.EncClassBanditWizard.FormKey,
                                Skyrim.Class.VendorTailor.FormKey,
                                Skyrim.Class.VendorApothecary.FormKey,
                                Skyrim.Class.VendorPawnbroker.FormKey,
                                Skyrim.Class.VendorSpells.FormKey,
                                Skyrim.Class.VendorFood.FormKey,
                                Skyrim.Class.Citizen.FormKey,
                                Skyrim.Class.GuardImperial.FormKey
                            }
                        }
                    }
                },
                new NPCAttribute()
                {
                    SubAttributes= new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeNPC()
                        {
                            Type= NPCAttributeType.NPC,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Npc.Nazeem.FormKey
                            }
                        }
                    }
                },
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeFactions()
                        {
                            Type = NPCAttributeType.Faction,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Faction.ServicesMarkarthCastleCook.FormKey
                            }
                        }
                    }
                }
            }
        };

        public static AttributeGroup CannotHaveScars = new AttributeGroup()
        {
            Label = "Cannot Have Scars",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeFactions()
                        {
                            Type = NPCAttributeType.Faction,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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

        public static AttributeGroup CanBeDirty = new AttributeGroup()
        {
            Label = "Can Be Dirty",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
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

        public static AttributeGroup MustBeDirty = new AttributeGroup()
        {
            Label = "Must Be Dirty",
            Attributes = new HashSet<NPCAttribute>()
            {
                new NPCAttribute()
                {
                    SubAttributes = new HashSet<ITypedNPCAttribute>()
                    {
                        new NPCAttributeClass()
                        {
                            Type = NPCAttributeType.Class,
                            FormKeys = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
                            {
                                Skyrim.Class.Beggar.FormKey,
                                Skyrim.Class.Miner.FormKey
                            }
                        }
                    }
                }
            }
        };
    }
}
