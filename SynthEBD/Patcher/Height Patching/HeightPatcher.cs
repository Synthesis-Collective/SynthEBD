using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class HeightPatcher
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly UniqueNPCData _uniqueNPCData;
    public HeightPatcher(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _uniqueNPCData = uniqueNPCData;
    }
    public float? AssignNPCHeight(NPCInfo npcInfo, HeightConfig heightConfig, ISkyrimMod outputMod)
    {
        Npc npc = null;
        float assignedHeight = 1;

        _logger.OpenReportSubsection("Height", npcInfo);
        _logger.LogReport("Assigning NPC height", false, npcInfo);

        if (!_patcherState.HeightSettings.bChangeNPCHeight)
        {
            _logger.LogReport("Height randomization for individual NPCs is disabled. Height remains: " + npcInfo.NPC.Height, false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            return null;
        }
        else if (!_patcherState.HeightSettings.bOverwriteNonDefaultNPCHeights && !npcInfo.NPC.Height.Equals(1))
        {
            _logger.LogReport("Height randomization is disabled for NPCs with custom height. Height remains: " + npcInfo.NPC.Height, false, npcInfo);
            _logger.CloseReportSubsection(npcInfo);
            return null;
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
                _logger.LogReport("No height configurations were installed.", false, npcInfo);
                _logger.CloseReportSubsection(npcInfo);
                return null;
            }

            var heightAssignment = heightConfig.HeightAssignments.Where(x => x.Races.Contains(npcInfo.HeightRace)).FirstOrDefault();

            if (heightAssignment == null)
            {
                _logger.LogReport("No heights were specified for NPCs of the current race.", false, npcInfo);
                _logger.CloseReportSubsection(npcInfo);
                return null;
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
            else if (_patcherState.GeneralSettings.bLinkNPCsWithSameName && npcInfo.IsValidLinkedUnique && _uniqueNPCData.TryGetUniqueNPCHeight(npcInfo, out var uniqueLinkedHeight, out string unqiueFounderNPC) && uniqueLinkedHeight != -1)
            {
                assignedHeight = uniqueLinkedHeight;
                _logger.LogReport("Another unique NPC with the same name (" + unqiueFounderNPC + ") was assigned height " + uniqueLinkedHeight.ToString() + ". Using that height for current NPC.", false, npcInfo);
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
        _logger.LogReport("Height set to: " + assignedHeight, false, npcInfo);

        if (_patcherState.GeneralSettings.bEnableConsistency)
        {
            npcInfo.ConsistencyNPCAssignment.Height = assignedHeight;
        }

        if (npcInfo.LinkGroupMember == NPCInfo.LinkGroupMemberType.Primary)
        {
            npcInfo.AssociatedLinkGroup.AssignedHeight = assignedHeight;
        }

        if (_patcherState.GeneralSettings.bLinkNPCsWithSameName)
        {
            _uniqueNPCData.InitializeUnsetUniqueNPCHeight(npcInfo, assignedHeight);
        }

        _logger.CloseReportSubsection(npcInfo);
        
        return assignedHeight;
    }

    public void AssignRacialHeight(HeightConfig heightConfig, ISkyrimMod outputMod)
    {
        Race patchedRace = null;
        HeightAssignment heightRacialSetting = null;
        RaceAlias raceAlias = null;

        if (heightConfig == null) { return; }
        if (!_patcherState.HeightSettings.bChangeRaceHeight) { return; }

        foreach (var race in _environmentProvider.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().WinningOverrides<IRaceGetter>())
        {
            patchedRace = null;
            raceAlias = _patcherState.GeneralSettings.RaceAliases.Where(x => x.Race == race.FormKey && x.bApplyToHeight).FirstOrDefault();
               
            if (raceAlias != null && _patcherState.GeneralSettings.PatchableRaces.Contains(raceAlias.AliasRace) && _environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceAlias.AliasRace, out var raceAliasGetter))
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
            else if (_patcherState.GeneralSettings.PatchableRaces.Contains(race.FormKey))
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