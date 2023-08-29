using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD;

public class DefaultRaceGroupings
{
    public static RaceGrouping Humanoid = new RaceGrouping()
    {
        Label = "Humanoid",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.ElderRace.FormKey,
            Skyrim.Race.NordRaceVampire.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
            Skyrim.Race.ElderRaceVampire.FormKey,
            Skyrim.Race.NordRaceAstrid.FormKey,
            Skyrim.Race.DA13AfflictedRace.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey,
            Dawnguard.Race.DLC1NordRace.FormKey
        }
    };

    public static RaceGrouping HumanoidPlayable = new RaceGrouping()
    {
        Label = "Humanoid Playable",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.ElderRace.FormKey,
            Skyrim.Race.NordRaceVampire.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
            Skyrim.Race.ElderRaceVampire.FormKey,
        }
    };

    public static RaceGrouping HumanoidPlayableNonVampire = new RaceGrouping()
    {
        Label = "Humanoid Playable Non-Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey
        }
    };

    public static RaceGrouping HumanoidNonVampire = new RaceGrouping()
    {
        Label = "Humanoid Non-Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.ElderRace.FormKey,
            Skyrim.Race.NordRaceAstrid.FormKey,
            Skyrim.Race.DA13AfflictedRace.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey,
            Dawnguard.Race.DLC1NordRace.FormKey
        }
    };

    public static RaceGrouping HumanoidVampire = new RaceGrouping()
    {
        Label = "Humanoid Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRaceVampire.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
            Skyrim.Race.ElderRaceVampire.FormKey
        }
    };

    public static RaceGrouping HumanoidYoung = new RaceGrouping()
    {
        Label = "Humanoid Young",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.NordRaceVampire.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
            Skyrim.Race.NordRaceAstrid.FormKey,
            Skyrim.Race.DA13AfflictedRace.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey,
            Dawnguard.Race.DLC1NordRace.FormKey
        }
    };

    public static RaceGrouping HumanoidYoungNonVampire = new RaceGrouping()
    {
        Label = "Humanoid Young Non-Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.NordRaceAstrid.FormKey,
            Skyrim.Race.DA13AfflictedRace.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey,
            Dawnguard.Race.DLC1NordRace.FormKey
        }
    };

    public static RaceGrouping HumanoidYoungVampire = new RaceGrouping()
    {
        Label = "Humanoid Young Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRaceVampire.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey
        }
    };

    public static RaceGrouping Elven = new RaceGrouping()
    {
        Label = "Elven",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey
        }
    };

    public static RaceGrouping ElvenNonVampire = new RaceGrouping()
    {
        Label = "Elven Non-Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.WoodElfRace.FormKey,
            Dawnguard.Race.SnowElfRace.FormKey
        }
    };

    public static RaceGrouping ElvenVampire = new RaceGrouping()
    {
        Label = "Elven Vampire",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.DarkElfRaceVampire.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey,
        }
    };

    public static RaceGrouping Breton = new RaceGrouping()
    {
        Label = "Breton",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.BretonRace.FormKey,
            Skyrim.Race.BretonRaceVampire.FormKey
        }
    };

    public static RaceGrouping DarkElf = new RaceGrouping()
    {
        Label = "Dark Elf",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.DarkElfRace.FormKey,
            Skyrim.Race.DarkElfRaceVampire.FormKey
        }
    };

    public static RaceGrouping HighElf = new RaceGrouping()
    {
        Label = "High Elf",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.HighElfRace.FormKey,
            Skyrim.Race.HighElfRaceVampire.FormKey
        }
    };

    public static RaceGrouping Imperial = new RaceGrouping()
    {
        Label = "Imperial",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.ImperialRace.FormKey,
            Skyrim.Race.ImperialRaceVampire.FormKey
        }
    };

    public static RaceGrouping Nord = new RaceGrouping()
    {
        Label = "Nord",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.NordRace.FormKey,
            Skyrim.Race.NordRaceVampire.FormKey,
            Dawnguard.Race.DLC1NordRace.FormKey
        }
    };

    public static RaceGrouping Orc = new RaceGrouping()
    {
        Label = "Orc",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.OrcRace.FormKey,
            Skyrim.Race.OrcRaceVampire.FormKey
        }
    };

    public static RaceGrouping Redguard = new RaceGrouping()
    {
        Label = "Redguard",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.RedguardRace.FormKey,
            Skyrim.Race.RedguardRaceVampire.FormKey
        }
    };

    public static RaceGrouping WoodElf = new RaceGrouping()
    {
        Label = "Wood Elf",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.WoodElfRace.FormKey,
            Skyrim.Race.WoodElfRaceVampire.FormKey
        }
    };

    public static RaceGrouping Elder = new RaceGrouping()
    {
        Label = "Elder",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.ElderRace.FormKey,
            Skyrim.Race.ElderRaceVampire.FormKey
        }
    };

    public static RaceGrouping Khajiit = new RaceGrouping()
    {
        Label = "Khajiit",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.KhajiitRace.FormKey,
            Skyrim.Race.KhajiitRaceVampire.FormKey
        }
    };

    public static RaceGrouping Argonian = new RaceGrouping()
    {
        Label = "Argonian",
        Races = new HashSet<Mutagen.Bethesda.Plugins.FormKey>()
        {
            Skyrim.Race.ArgonianRace.FormKey,
            Skyrim.Race.ArgonianRaceVampire.FormKey
        }
    };
}