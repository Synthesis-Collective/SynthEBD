using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using static System.Windows.Forms.AxHost;

namespace SynthEBD;

public class NPCInfo
{
    public NPCInfo(INpcGetter npc, HashSet<LinkedNPCGroup> definedLinkGroups, HashSet<LinkedNPCGroupInfo> createdLinkGroupInfos, HashSet<NPCAssignment> specificNPCAssignments, Dictionary<string, NPCAssignment> consistency, BlockList blockList)
    {
        this.NPC = npc;
        this.LogIDstring = Logger.GetNPCLogNameString(npc);
        this.Gender = GetGender(npc);
        AssetsRace = AliasHandler.GetAliasTexMesh(npc.Race.FormKey);
        BodyShapeRace = AliasHandler.GetAliasBodyGen(npc.Race.FormKey);
        HeightRace = AliasHandler.GetAliasHeight(npc.Race.FormKey);
        HeadPartsRace = AliasHandler.GetAliasHeadParts(npc.Race.FormKey);

        IsPatchable = PatcherSettings.General.PatchableRaces.Contains(AssetsRace) || PatcherSettings.General.PatchableRaces.Contains(BodyShapeRace) || PatcherSettings.General.PatchableRaces.Contains(HeightRace);
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

        SpecificNPCAssignment = specificNPCAssignments.Where(x => x.NPCFormKey == npc.FormKey).FirstOrDefault();

        if (consistency.ContainsKey(this.NPC.FormKey.ToString()))
        {
            ConsistencyNPCAssignment = consistency[this.NPC.FormKey.ToString()];
        }
        else
        {
            ConsistencyNPCAssignment = new NPCAssignment();
            ConsistencyNPCAssignment.NPCFormKey = NPC.FormKey;
            ConsistencyNPCAssignment.DispName = LogIDstring;
            consistency.Add(this.NPC.FormKey.ToString(), ConsistencyNPCAssignment);
        }

        if (PatcherSettings.General.bChangeHeadParts)
        {
            foreach (var headpartFK in npc.HeadParts)
            {
                if (PatcherEnvironmentProvider.Instance.Environment.LinkCache.TryResolve<IHeadPartGetter>(headpartFK.FormKey, out IHeadPartGetter headPart))
                {
                    ExistingHeadParts.Add(headPart);
                }
            }
        }

        BlockedNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(blockList, npc.FormKey);
        BlockedPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(blockList, npc.FormKey);

        Report = new Logger.NPCReport(this);
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