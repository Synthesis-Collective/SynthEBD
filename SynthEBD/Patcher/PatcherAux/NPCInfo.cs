using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

/// <summary>
/// Per-NPC context object threaded through the patcher's assignment axes. Resolves the NPC's gender,
/// per-axis (alias-mapped) races, patchability, linked-group membership, unique-NPC status, and any
/// specific/consistency assignments, and caches existing head parts and block-list status. Equality is
/// defined by the original NPC record.
/// </summary>
public class NPCInfo : IEquatable<NPCInfo>
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AliasHandler _aliasHandler;
    private readonly UniqueNPCData _uniqueNPCData;

    /// <summary>Autofac factory delegate for constructing an <see cref="NPCInfo"/> with its non-injected arguments.</summary>
    public delegate NPCInfo Factory(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos);

    /// <summary>
    /// Builds the per-NPC context: resolves gender and per-axis races, determines patchability (short-circuiting
    /// if the NPC is not patchable), wires up linked-group info, unique status, specific/consistency assignments,
    /// existing head parts, and block-list status.
    /// </summary>
    public NPCInfo(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AliasHandler aliasHandler, UniqueNPCData uniqueNPCData)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _aliasHandler = aliasHandler;
        _uniqueNPCData = uniqueNPCData;

        NPC = npc;
        OriginalNPC = npc;
        LogIDstring = _logger.GetNPCLogNameString(npc);
        Gender = GetGender(npc);
        AssetsRace = _aliasHandler.GetAliasTexMesh(npc.Race.FormKey);
        BodyShapeRace = _aliasHandler.GetAliasBodyGen(npc.Race.FormKey);
        HeightRace = _aliasHandler.GetAliasHeight(npc.Race.FormKey);
        HeadPartsRace = _aliasHandler.GetAliasHeadParts(npc.Race.FormKey);
        Report = new Logger.NPCReport(this);

        IsPatchable = _patcherState.GeneralSettings.PatchableRaces.Contains(AssetsRace) || _patcherState.GeneralSettings.PatchableRaces.Contains(BodyShapeRace) || _patcherState.GeneralSettings.PatchableRaces.Contains(HeightRace) || _patcherState.GeneralSettings.PatchableRaces.Contains(HeadPartsRace);
        if (!IsPatchable)
        {
            return;
        }

        AssociatedLinkGroup = SearchLinkedInfoFromList(npc.FormKey);
        if (AssociatedLinkGroup == null)
        {
            AssociatedLinkGroup = LinkedNPCGroupInfo.GetInfoFromLinkedNPCGroup(definedLinkGroups, createdLinkGroupInfos, npc.FormKey);
            if (AssociatedLinkGroup != null)
            {
                AllLinkedNPCGroupInfos.Add(AssociatedLinkGroup);
            }
        }
        if (AssociatedLinkGroup != null)
        {
            if (AssociatedLinkGroup.PrimaryNPCFormKey.ToString() == NPC.FormKey.ToString())
            {
                LinkGroupMember = LinkGroupMemberType.Primary;
            }
            else
            {
                LinkGroupMember = LinkGroupMemberType.Secondary;
            }
        }

        IsValidLinkedUnique = _uniqueNPCData.IsValidUnique(npc, out var npcName);
        Name = npcName;

        SpecificNPCAssignment = _patcherState.SpecificNPCAssignments.FirstOrDefault(x => x.NPCFormKey == npc.FormKey);

        ConsistencyNPCAssignment = ResolveConsistencyAssignment(_patcherState.Consistency, NPC.FormKey, LogIDstring);

        if (_patcherState.GeneralSettings.bChangeHeadParts)
        {
            foreach (var headpartFK in npc.HeadParts)
            {
                if (_environmentProvider.LinkCache.TryResolve<IHeadPartGetter>(headpartFK.FormKey, out IHeadPartGetter headPart))
                {
                    ExistingHeadParts.Add(headPart);
                }
            }
        }

        BlockedNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(_patcherState.BlockList, npc.FormKey);
        BlockedPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(_patcherState.BlockList, npc.FormKey, _environmentProvider.LinkCache);
    }

    /// <summary>The NPC record being patched (may be swapped to a deep copy during patching).</summary>
    public INpcGetter NPC { get; set; }
    /// <summary>The original NPC record before any deep-copy substitution.</summary>
    public INpcGetter OriginalNPC { get; set; } // NPC may be changed if patcer deep copies in the NPC
    /// <summary>Display name resolved for the NPC (used for unique tracking and logging).</summary>
    public string Name { get; set; }
    /// <summary>Human-readable identifier string used in log output.</summary>
    public string LogIDstring { get; set; }
    /// <summary>The NPC's gender.</summary>
    public Gender Gender { get; set; }
    /// <summary>The alias-mapped race used for asset (texture/mesh) selection.</summary>
    public FormKey AssetsRace { get; set; }
    /// <summary>The alias-mapped race used for body-shape selection.</summary>
    public FormKey BodyShapeRace { get; set; }
    /// <summary>The alias-mapped race used for height assignment.</summary>
    public FormKey HeightRace { get; set; }
    /// <summary>The alias-mapped race used for head-part selection.</summary>
    public FormKey HeadPartsRace { get; set; }
    /// <summary>Whether any of the NPC's per-axis races is in the patchable-races set.</summary>
    public bool IsPatchable { get; set; }
    /// <summary>The linked-NPC-group info this NPC belongs to, or null if unlinked.</summary>
    public LinkedNPCGroupInfo AssociatedLinkGroup { get; set; }
    /// <summary>This NPC's role within its linked group (none, primary, or secondary).</summary>
    public LinkGroupMemberType LinkGroupMember { get; set; } = LinkGroupMemberType.None;
    /// <summary>Whether the NPC is a valid linked unique (shares appearance across instances of the same unique character).</summary>
    public bool IsValidLinkedUnique { get; set; }
    /// <summary>A user-defined forced assignment targeting this specific NPC, if any.</summary>
    public NPCAssignment SpecificNPCAssignment { get; set; }
    /// <summary>The NPC's consistency record (previous-run assignments), created if not already present.</summary>
    public NPCAssignment ConsistencyNPCAssignment { get; set; }

    /// <summary>
    /// Resolves (and guarantees non-null) the consistency assignment for an NPC: returns the existing entry when one
    /// is present and non-null, otherwise creates a fresh assignment seeded with the NPC's FormKey/display name and
    /// stores it. A present-but-null entry (e.g. a corrupted/hand-edited consistency file) is treated as "no
    /// consistency" and overwritten via the indexer, so every consistency write site can deref this without a null
    /// guard. A fresh assignment is the canonical "no consistency yet" state — identical to a first-run NPC — so this
    /// is behavior-preserving; its field defaults (Height null, BodySlidePreset "", etc.) are the read-side sentinels.
    /// </summary>
    public static NPCAssignment ResolveConsistencyAssignment(Dictionary<string, NPCAssignment> consistency, FormKey npcFormKey, string dispName)
    {
        var key = npcFormKey.ToString();
        if (consistency.TryGetValue(key, out var existing) && existing != null)
        {
            return existing;
        }

        var assignment = new NPCAssignment
        {
            NPCFormKey = npcFormKey,
            DispName = dispName,
        };
        // THREADING (future parallel selection): writes to the shared _patcherState.Consistency dict during selection; a
        // parallel per-NPC loop needs a concurrent dict or synchronization here (keys are per-NPC, so a ConcurrentDictionary suffices).
        consistency[key] = assignment; // indexer (not Add) so a present-but-null entry is overwritten rather than throwing
        return assignment;
    }

    /// <summary>Per-NPC report accumulator for logging assignment decisions.</summary>
    public Logger.NPCReport Report { get; set; }
    /// <summary>The NPC's currently assigned head parts, resolved from the link cache (only populated when head-part patching is enabled).</summary>
    public HashSet<IHeadPartGetter> ExistingHeadParts { get; set; } = new();
    /// <summary>The NPC's entry in the block list, if it is individually blocked.</summary>
    public BlockedNPC BlockedNPCEntry { get; set; }
    /// <summary>The block-list entry for the NPC's source plugin, if its plugin is blocked.</summary>
    public BlockedPlugin BlockedPluginEntry { get; set; }

    /// <summary>An NPC's role within a linked NPC group.</summary>
    public enum LinkGroupMemberType
    {
        /// <summary>Not a member of any linked group.</summary>
        None,
        /// <summary>The primary member, whose assignments drive the group.</summary>
        Primary,
        /// <summary>A secondary member, which inherits the primary's assignments.</summary>
        Secondary
    }

    /// <summary>Returns the NPC's gender based on its configuration's Female flag.</summary>
    /// <param name="npc">The NPC to inspect.</param>
    /// <returns><see cref="Gender.Female"/> if the Female flag is set; otherwise <see cref="Gender.Male"/>.</returns>
    public static Gender GetGender(INpcGetter npc)
    {
        if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
        {
            return Gender.Female;
        }

        return Gender.Male;
    }

    /// <summary>Per-run cache of all linked-group infos created so far, shared across NPCInfo instances so linked NPCs
    /// resolve to the same group. Reset at the start of each patcher run via <see cref="ResetLinkGroupCache"/> (it is
    /// searched before the current settings, so a stale entry from a prior run would otherwise be reused).</summary>
    // THREADING (future parallel selection): shared coordination state read+written during selection
    // (SearchLinkedInfoFromList + the ctor Add). Parallelizing the per-NPC loop requires synchronizing access here, or
    // partitioning linked groups so all members of a group run on the same thread -- linked-group resolution is cross-NPC.
    private static HashSet<LinkedNPCGroupInfo> AllLinkedNPCGroupInfos = new HashSet<LinkedNPCGroupInfo>();

    /// <summary>Clears the per-run linked-group cache. Called at the start of each patcher run so a re-run in the same
    /// app session does not reuse stale group infos (or grow the set unbounded). Within a run it accumulates as intended.</summary>
    public static void ResetLinkGroupCache() => AllLinkedNPCGroupInfos.Clear();

    /// <summary>Searches the static cache for a linked-group info that already contains the given NPC form key.</summary>
    /// <param name="currentFormKey">The NPC form key to look up.</param>
    /// <returns>The matching group info, or null if none contains the form key.</returns>
    private static LinkedNPCGroupInfo SearchLinkedInfoFromList(FormKey currentFormKey)
    {
        foreach (var l in AllLinkedNPCGroupInfos)
        {
            if (l.NPCFormKeys.Contains(currentFormKey))
            {
                return l;
            }
        }
        return null;
    }
    
    /// <summary>Two <see cref="NPCInfo"/> instances are equal when they wrap the same original NPC record.</summary>
    public bool Equals(NPCInfo other)
    {
        if (other == null) return false;
        return Equals(this.OriginalNPC, other.OriginalNPC);
    }

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as NPCInfo);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return OriginalNPC?.GetHashCode() ?? 0;
    }
}