using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Read-only helper for picking preview NPCs for the 3D viewer outside of
/// Specific NPC Assignment contexts. Not bound to patching state.
/// </summary>
public class PreviewNpcResolver
{
    private readonly IEnvironmentStateProvider _env;
    private readonly Logger _logger;

    public PreviewNpcResolver(IEnvironmentStateProvider env, Logger logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Returns the FormKey of the first NPC in load-order priority whose
    /// Race matches <paramref name="race"/> and whose gender matches
    /// <paramref name="gender"/>. Returns FormKey.Null if none found.
    /// </summary>
    public FormKey FindFirstNpcForRace(FormKey race, Gender gender)
    {
        if (_env.LoadOrder == null) return FormKey.Null;

        foreach (var ctx in _env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides())
        {
            var npc = ctx.Record;
            if (npc.Race == null || npc.Race.IsNull) continue;
            if (!npc.Race.FormKey.Equals(race)) continue;
            if (GetGender(npc) != gender) continue;
            return npc.FormKey;
        }
        return FormKey.Null;
    }

    /// <summary>
    /// Returns the first NordRace NPC of the given gender. Convenience
    /// default for preview mappings where no per-race NPC is set.
    /// </summary>
    public FormKey FindFirstNordRaceNpc(Gender gender)
    {
        return FindFirstNpcForRace(Skyrim.Race.NordRace.FormKey, gender);
    }

    /// <summary>
    /// Returns the first NPC in load-order priority that satisfies the BodySlide preview
    /// picker's selection criteria:
    /// <list type="number">
    ///   <item>matches <paramref name="gender"/>;</item>
    ///   <item>has <c>NPC.Weight</c> within <paramref name="tolerance"/> of <paramref name="targetWeight"/>;</item>
    ///   <item>has the <see cref="NpcConfiguration.Flag.Unique"/> flag — generic actors
    ///         (bandits, civilians, etc.) get filtered out so the preview lands on a
    ///         named NPC with a recognizable identity;</item>
    ///   <item>its race FormKey originates in <c>Skyrim.esm</c> — exotic / DLC / mod-added
    ///         races aren't usable as previews because their meshes / FaceGen aren't
    ///         shared with the player's BodySlide build;</item>
    ///   <item>that race carries the <c>ActorTypeNPC</c> keyword — filters out creature-
    ///         like races whose body topology won't match standard CBBE / UNP /
    ///         3BA / etc.</item>
    /// </list>
    /// Returns <see cref="FormKey.Null"/> with a diagnostic log line (including
    /// per-criterion rejection counts) when nothing matches.
    /// </summary>
    public FormKey FindFirstNpcAtWeight(Gender gender, int targetWeight, int tolerance = 0)
    {
        if (_env.LoadOrder == null || _env.LinkCache == null) return FormKey.Null;

        // String-compare ModKey.ToString() against "Skyrim.esm" — robust against any
        // surprise in the FormKeys.SkyrimSE namespace's static surface (an earlier attempt
        // used `Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.ModKey` which silently failed to
        // compare against real ModKey structs, rejecting every NPC). The IFormLinkGetter
        // for the ActorTypeNPC keyword IS usable with Contains because the comparer
        // resolves through FormKey equality.
        var actorTypeNpcLink = Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Keyword.ActorTypeNPC;

        // Vanilla body-mesh + skeleton paths. The chosen NPC's worn-armor body ARMA
        // (or, when the NPC's WNAM is empty, its race's Skin armor) must resolve to
        // one of these — non-vanilla overrides (CBBE 3BA armatures, beast-race
        // bodies, custom skeletons) won't share topology with the BodySlide build
        // the viewer renders, so they're filtered out of Auto picks.
        // The paths are compared against ARMA.WorldModel.<gender>.File.GivenPath
        // and Race.SkeletalModel.<gender>.File.GivenPath, which by Skyrim convention
        // are stored relative to Data\meshes\ (no "meshes\" prefix). Normalized
        // case-insensitively + forward slashes coerced to backslashes before compare.
        string expectedBody = gender == Gender.Female
            ? @"actors\character\character assets\femalebody_1.nif"
            : @"actors\character\character assets\malebody_1.nif";
        string expectedSkel = gender == Gender.Female
            ? @"actors\character\character assets female\skeleton_female.nif"
            : @"actors\character\character assets\skeleton.nif";

        int scanned = 0;
        int rejGender = 0;
        int rejWeight = 0;
        int rejUnique = 0;
        int rejRaceNull = 0;
        int rejRaceMod = 0;
        int rejRaceUnresolved = 0;
        int rejRaceKeyword = 0;
        int rejBodyPath = 0;
        int rejSkeletonPath = 0;

        foreach (var ctx in _env.LoadOrder.PriorityOrder.Npc().WinningContextOverrides())
        {
            scanned++;
            var npc = ctx.Record;

            if (GetGender(npc) != gender) { rejGender++; continue; }
            if (Math.Abs(npc.Weight - targetWeight) > tolerance) { rejWeight++; continue; }
            if (!npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique)) { rejUnique++; continue; }

            if (npc.Race == null || npc.Race.IsNull) { rejRaceNull++; continue; }
            if (!string.Equals(npc.Race.FormKey.ModKey.ToString(), "Skyrim.esm", System.StringComparison.OrdinalIgnoreCase))
            {
                rejRaceMod++;
                continue;
            }

            if (!_env.LinkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var raceGetter))
            {
                rejRaceUnresolved++;
                continue;
            }
            if (raceGetter.Keywords == null || !raceGetter.Keywords.Contains(actorTypeNpcLink))
            {
                rejRaceKeyword++;
                continue;
            }

            // Body-mesh path: walk WornArmor (or Race.Skin fallback) → first Body-slot
            // ARMA applicable to the NPC's race → WorldModel.<gender>.File.GivenPath.
            string? bodyPath = ResolveBodyMeshGivenPath(npc, raceGetter, gender);
            if (bodyPath == null || !NormalizedPathEquals(bodyPath, expectedBody))
            {
                rejBodyPath++;
                continue;
            }

            // Skeleton path: Race.SkeletalModel.<gender>.File.GivenPath.
            string? skelPath = ResolveSkeletonGivenPath(raceGetter, gender);
            if (skelPath == null || !NormalizedPathEquals(skelPath, expectedSkel))
            {
                rejSkeletonPath++;
                continue;
            }

            return npc.FormKey;
        }

        _logger.LogMessage(
            $"PreviewNpcResolver: No NPC found at weight {targetWeight} (±{tolerance}) for gender {gender}. " +
            $"Scanned {scanned} NPC(s); rejected — gender:{rejGender}, weight:{rejWeight}, " +
            $"unique:{rejUnique}, race-null:{rejRaceNull}, race-not-Skyrim.esm:{rejRaceMod}, " +
            $"race-unresolved:{rejRaceUnresolved}, race-missing-ActorTypeNPC:{rejRaceKeyword}, " +
            $"body-path-not-vanilla:{rejBodyPath}, skeleton-path-not-vanilla:{rejSkeletonPath}.");
        return FormKey.Null;
    }

    /// <summary>
    /// Mirrors <c>NpcMeshResolver</c>'s worn-armor walk to fetch the Body-slot ARMA's
    /// gender-specific WorldModel path. Returns null when any link in the chain can't
    /// resolve or no Body-slot ARMA is applicable to the NPC's race.
    /// </summary>
    private string? ResolveBodyMeshGivenPath(INpcGetter npc, IRaceGetter raceGetter, Gender gender)
    {
        IArmorGetter? armor = null;
        if (npc.WornArmor != null && !npc.WornArmor.IsNull)
        {
            _env.LinkCache.TryResolve<IArmorGetter>(npc.WornArmor.FormKey, out armor);
        }
        if (armor == null && raceGetter.Skin != null && !raceGetter.Skin.IsNull)
        {
            _env.LinkCache.TryResolve<IArmorGetter>(raceGetter.Skin.FormKey, out armor);
        }
        if (armor?.Armature == null) return null;

        FormKey raceKey = raceGetter.FormKey;
        foreach (var armaLink in armor.Armature)
        {
            if (!_env.LinkCache.TryResolve<IArmorAddonGetter>(armaLink.FormKey, out var arma)) continue;
            if (arma.BodyTemplate == null) continue;
            if (!arma.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body)) continue;

            // ARMA-race applicability (same rule the renderer uses):
            // arma.Race == npc.race OR arma.AdditionalRaces contains npc.race.
            bool raceMatch = arma.Race != null && !arma.Race.IsNull && arma.Race.FormKey.Equals(raceKey);
            if (!raceMatch && arma.AdditionalRaces != null)
            {
                foreach (var addRace in arma.AdditionalRaces)
                {
                    if (!addRace.IsNull && addRace.FormKey.Equals(raceKey)) { raceMatch = true; break; }
                }
            }
            if (!raceMatch) continue;

            var model = gender == Gender.Female ? arma.WorldModel?.Female : arma.WorldModel?.Male;
            string? path = model?.File?.GivenPath;
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// Race-driven skeleton NIF path lookup. Same source NpcMeshResolver uses for CPU
    /// skinning. Returns null when the race has no SkeletalModel entry for the gender.
    /// </summary>
    private static string? ResolveSkeletonGivenPath(IRaceGetter raceGetter, Gender gender)
    {
        if (raceGetter.SkeletalModel == null) return null;
        var skel = gender == Gender.Female ? raceGetter.SkeletalModel.Female : raceGetter.SkeletalModel.Male;
        string? path = skel?.File?.GivenPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>
    /// Normalize-then-compare for the body / skeleton path check. Vanilla paths are
    /// stored without the <c>meshes\</c> prefix that the renderer prepends downstream,
    /// but real NIF GivenPath values occasionally vary in case and slash direction —
    /// strip any leading <c>meshes\</c>, coerce forward slashes to backslashes, and
    /// compare case-insensitively.
    /// </summary>
    private static bool NormalizedPathEquals(string candidate, string expected)
    {
        string norm = candidate.Replace('/', '\\').TrimStart('\\');
        if (norm.StartsWith(@"meshes\", System.StringComparison.OrdinalIgnoreCase))
            norm = norm.Substring("meshes\\".Length);
        return string.Equals(norm, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the weight of the NPC (0-100) if resolvable, else null.
    /// </summary>
    public float? GetNpcWeight(FormKey npc)
    {
        if (npc.IsNull || _env.LinkCache == null) return null;
        if (_env.LinkCache.TryResolve<INpcGetter>(npc, out var rec))
        {
            return rec.Weight;
        }
        return null;
    }

    private static Gender GetGender(INpcGetter npc)
    {
        return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)
            ? Gender.Female
            : Gender.Male;
    }
}
