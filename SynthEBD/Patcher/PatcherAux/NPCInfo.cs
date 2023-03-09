using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace SynthEBD;

public class NPCInfo
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AliasHandler _aliasHandler;

    public delegate NPCInfo Factory(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos);
    
    public NPCInfo(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AliasHandler aliasHandler)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _aliasHandler = aliasHandler;

        NPC = npc;
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

        IsValidLinkedUnique = UniqueNPCData.IsValidUnique(npc, out var npcName);
        Name = npcName;

        SpecificNPCAssignment = _patcherState.SpecificNPCAssignments.Where(x => x.NPCFormKey == npc.FormKey).FirstOrDefault();

        if (_patcherState.Consistency.ContainsKey(NPC.FormKey.ToString()))
        {
            ConsistencyNPCAssignment = _patcherState.Consistency[NPC.FormKey.ToString()];
        }
        else
        {
            ConsistencyNPCAssignment = new NPCAssignment();
            ConsistencyNPCAssignment.NPCFormKey = NPC.FormKey;
            ConsistencyNPCAssignment.DispName = LogIDstring;
            _patcherState.Consistency.Add(NPC.FormKey.ToString(), ConsistencyNPCAssignment);
        }

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

    public INpcGetter NPC { get; set; }
    public string Name { get; set; }
    public string LogIDstring { get; set; }
    public Gender Gender { get; set; }
    public FormKey AssetsRace { get; set; }
    public FormKey BodyShapeRace { get; set; }
    public FormKey HeightRace { get; set; }
    public FormKey HeadPartsRace { get; set; }
    public bool IsPatchable { get; set; }
    public LinkedNPCGroupInfo AssociatedLinkGroup { get; set; }
    public LinkGroupMemberType LinkGroupMember { get; set; } = LinkGroupMemberType.None;
    public bool IsValidLinkedUnique { get; set; }
    public NPCAssignment SpecificNPCAssignment { get; set; }
    public NPCAssignment ConsistencyNPCAssignment { get; set; }
    public Logger.NPCReport Report { get; set; }
    public HashSet<IHeadPartGetter> ExistingHeadParts { get; set; } = new();
    public BlockedNPC BlockedNPCEntry { get; set; }
    public BlockedPlugin BlockedPluginEntry { get; set; }

    public enum LinkGroupMemberType
    {
        None,
        Primary,
        Secondary
    }

    public static Gender GetGender(INpcGetter npc)
    {
        if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female))
        {
            return Gender.Female;
        }

        return Gender.Male;
    }

    private static HashSet<LinkedNPCGroupInfo> AllLinkedNPCGroupInfos = new HashSet<LinkedNPCGroupInfo>();

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
}