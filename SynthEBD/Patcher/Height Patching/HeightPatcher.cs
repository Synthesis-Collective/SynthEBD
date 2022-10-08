using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeightPatcher
{
    public static void AssignNPCHeight(NPCInfo npcInfo, HeightConfig heightConfig, SkyrimMod outputMod)
    {
        Npc npc = null;
        float assignedHeight = 1;

        Logger.OpenReportSubsection("Height", npcInfo);
        Logger.LogReport("Assigning NPC height", false, npcInfo);

        if (!PatcherSettings.Height.bChangeNPCHeight)
        {
            Logger.LogReport("Height randomization for individual NPCs is disabled. Height remains: " + npcInfo.NPC.Height, false, npcInfo);
            Logger.CloseReportSubsection(npcInfo);
            return;
        }
        else if (!PatcherSettings.Height.bOverwriteNonDefaultNPCHeights && !npcInfo.NPC.Height.Equals(1))
        {
            Logger.LogReport("Height randomization is disabled for NPCs with custom height. Height remains: " + npcInfo.NPC.Height, false, npcInfo);
            Logger.CloseReportSubsection(npcInfo);
            return;
        }
        else if (npcInfo.SpecificNPCAssignment != null && npcInfo.SpecificNPCAssignment.Height != null)
        {
            npc = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);
            assignedHeight = npcInfo.SpecificNPCAssignment.Height.Value;
        }
        else
        {
            if (heightConfig is null)
            {
                Logger.LogReport("No height configurations were installed.", false, npcInfo);
                Logger.CloseReportSubsection(npcInfo);
                return;
            }

            var heightAssignment = heightConfig.HeightAssignments.Where(x => x.Races.Contains(npcInfo.HeightRace)).FirstOrDefault();

            if (heightAssignment == null)
            {
                Logger.LogReport("No heights were specified for NPCs of the current race.", false, npcInfo);
                Logger.CloseReportSubsection(npcInfo);
                return;
            }

            npc = outputMod.Npcs.GetOrAddAsOverride(npcInfo.NPC);

            float lowerBound = 0;
            float upperBound = 0;
            float range = 0;

            switch (npcInfo.Gender)
            {
                case Gender.Male:
                    lowerBound = 1 - heightAssignment.HeightMaleRange;
                    upperBound = 1 + heightAssignment.HeightMaleRange;
                    range = heightAssignment.HeightMaleRange;
                    break;
                case Gender.Female:
                    lowerBound = 1 - heightAssignment.HeightFemaleRange;
                    upperBound = 1 + heightAssignment.HeightFemaleRange;
                    range = heightAssignment.HeightFemaleRange;
                    break;
            }

            // assign linked height if necessary
            if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Secondary)
            {
                assignedHeight = npcInfo.AssociatedLinkGroup.AssignedHeight;
            }
            else if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.Height) != -1)
            {
                assignedHeight = Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedHeight;
                Logger.LogReport("Another unique NPC with the same name was assigned a height. Using that height for current NPC.", false, npcInfo);
            }
            // assign by consistency if possible
            else if (npcInfo.ConsistencyNPCAssignment != null && npcInfo.ConsistencyNPCAssignment.Height != null && npcInfo.ConsistencyNPCAssignment.Height <= upperBound && npcInfo.ConsistencyNPCAssignment.Height >= lowerBound)
            {
                assignedHeight = npcInfo.ConsistencyNPCAssignment.Height.Value;
            }
            // assign random
            else
            {
                var rand = new Random();
                switch (heightAssignment.DistributionMode)
                {
                    case DistMode.uniform:
                        double randomVal = rand.NextDouble() * (upperBound - lowerBound) + lowerBound;
                        assignedHeight = (float)randomVal;
                        break;

                    case DistMode.bellCurve: //https://stackoverflow.com/a/218600
                        int mean = 1;
                        double stDev = range / 3; // range is 3 sigma
                        // Box Muller Transform
                        double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
                        double u2 = 1.0 - rand.NextDouble();
                        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
                        double randNormal = mean + stDev * randStdNormal; //random normal(mean,stdDev^2)

                        // bound distribution to avoid crazy random values
                        if (randNormal > upperBound) { randNormal = upperBound; }
                        else if (randNormal < lowerBound) { randNormal = lowerBound; }

                        assignedHeight = (float)randNormal;
                        break;
                }
            }
        }

        npc.Height = assignedHeight;
        Logger.LogReport("Height set to: " + assignedHeight, false, npcInfo);

        if (PatcherSettings.General.bEnableConsistency)
        {
            npcInfo.ConsistencyNPCAssignment.Height = assignedHeight;
        }

        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedHeight = assignedHeight;
        }

        if (PatcherSettings.General.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && UniqueNPCData.GetUniqueNPCTrackerData(npcInfo, AssignmentType.Height) == -1)
        {
            Patcher.UniqueAssignmentsByName[npcInfo.Name][npcInfo.Gender].AssignedHeight = assignedHeight;
        }

        Logger.CloseReportSubsection(npcInfo);
    }

    public static void AssignRacialHeight(HeightConfig heightConfig, SkyrimMod outputMod)
    {
        Race patchedRace = null;
        HeightAssignment heightRacialSetting = null;
        RaceAlias raceAlias = null;

        if (heightConfig == null) { return; }
        if (!PatcherSettings.Height.bChangeRaceHeight) { return; }

        foreach (var race in PatcherEnvironmentProvider.Instance.Environment.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<IRaceGetter>())
        {
            patchedRace = null;
            raceAlias = PatcherSettings.General.RaceAliases.Where(x => x.Race == race.FormKey && x.bApplyToHeight).FirstOrDefault();
               
            if (raceAlias != null && PatcherSettings.General.PatchableRaces.Contains(raceAlias.AliasRace) && PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IRaceGetter>(raceAlias.AliasRace, out var raceAliasGetter))
            {
                heightRacialSetting = heightConfig.HeightAssignments.Where(x => x.Races.Contains(raceAlias.AliasRace)).FirstOrDefault();

                if (heightRacialSetting == null || heightRacialSetting.HeightMale == race.Height.Male && heightRacialSetting.HeightFemale == race.Height.Female)
                {
                    continue; // avoid creating ITM
                }
                else
                {
                    patchedRace = outputMod.Races.GetOrAddAsOverride(raceAliasGetter);
                }
            }
            else if (PatcherSettings.General.PatchableRaces.Contains(race.FormKey))
            {
                heightRacialSetting = heightConfig.HeightAssignments.Where(x => x.Races.Contains(race.FormKey)).FirstOrDefault();
                    
                if (heightRacialSetting == null || heightRacialSetting.HeightMale == race.Height.Male && heightRacialSetting.HeightFemale == race.Height.Female)
                {
                    continue; // avoid creating ITM
                }
                else
                {
                    patchedRace = outputMod.Races.GetOrAddAsOverride(race);
                }
            }

            if (patchedRace != null)
            {
                if (raceAlias != null)
                {
                    if (raceAlias.bMale)
                    {
                        patchedRace.Height.Male = heightRacialSetting.HeightMale;
                    }
                    if (raceAlias.bFemale)
                    {
                        patchedRace.Height.Female = heightRacialSetting.HeightFemale;
                    }
                }
                else
                {
                    patchedRace.Height.Male = heightRacialSetting.HeightMale;
                    patchedRace.Height.Female = heightRacialSetting.HeightFemale;
                }
            }
        }
    }
}