using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class BlockList
    {
        public BlockList()
        {
            this.NPCs = new HashSet<BlockedNPC>();
            this.Plugins = new HashSet<BlockedPlugin>();
        }

        public HashSet<BlockedNPC> NPCs { get; set; }
        public HashSet<BlockedPlugin> Plugins { get; set; }
    }

    public class BlockedNPC
    {
        public BlockedNPC()
        {
            this.FormKey = new FormKey();
            this.Assets = true;
            this.Height = false;
            this.BodyGen = false;
        }

        public FormKey FormKey { get; set; }
        public bool Assets { get; set; }
        public bool Height { get; set; }
        public bool BodyGen { get; set; }
    }

    public class BlockedPlugin
    {
        public BlockedPlugin()
        {
            this.ModKey = new ModKey();
            this.Assets = true;
            this.Height = false;
            this.BodyGen = false;
        }

        public ModKey ModKey { get; set; }
        public bool Assets { get; set; }
        public bool Height { get; set; }
        public bool BodyGen { get; set; }
    }

    public class zEBDBlockList
    {
        public zEBDBlockList()
        {
            this.blockedNPCs = new HashSet<zEBDBlockedNPC>();
            this.blockedPlugins = new HashSet<zEBDBlockedPlugin>();
        }
        public HashSet<zEBDBlockedNPC> blockedNPCs { get; set; }
        public HashSet<zEBDBlockedPlugin> blockedPlugins { get; set; }
        public static BlockList ToSynthEBD(zEBDBlockList zList)
        {
            BlockList sList = new BlockList();
            var env = new GameEnvironmentProvider().MyEnvironment;

            foreach (var npc in zList.blockedNPCs)
            {
                BlockedNPC blockedNPC = new BlockedNPC();
                blockedNPC.FormKey = Converters.zEBDSignatureToFormKey(npc.rootPlugin, npc.formID, env);
                blockedNPC.Assets = npc.bBlockAssets;
                blockedNPC.Height = npc.bBlockHeight;
                blockedNPC.BodyGen = npc.bBlockBodyGen;
                sList.NPCs.Add(blockedNPC);
            }

            foreach (var plugin in zList.blockedPlugins)
            {
                BlockedPlugin blockedPlugin = new BlockedPlugin();
                blockedPlugin.ModKey = ModKey.FromNameAndExtension(plugin.name);
                blockedPlugin.Assets = plugin.bBlockAssets;
                blockedPlugin.Height = plugin.bBlockHeight;
                blockedPlugin.BodyGen = plugin.bBlockBodyGen;
                sList.Plugins.Add(blockedPlugin);
            }

            return sList;
        }
    }

    public class zEBDBlockedNPC
    {
        public zEBDBlockedNPC()
        {
            this.name = "";
            this.formID = "";
            this.EDID = "";
            this.rootPlugin = "";
            this.displayString = "";
            this.bBlockAssets = true;
            this.bBlockHeight = false;
            this.bBlockBodyGen = false;
        }

        public string name { get; set; }
        public string formID { get; set; }
        public string EDID { get; set; }
        public string rootPlugin { get; set; }
        public string displayString { get; set; }
        public bool bBlockAssets { get; set; }
        public bool bBlockHeight { get; set; }
        public bool bBlockBodyGen { get; set; }
    }

    public class zEBDBlockedPlugin
    {
        public zEBDBlockedPlugin()
        {
            this.name = "";
            this.bBlockAssets = true;
            this.bBlockHeight = false;
            this.bBlockBodyGen = false;
        }
        public string name { get; set; }
        public bool bBlockAssets { get; set; }
        public bool bBlockHeight { get; set; }
        public bool bBlockBodyGen { get; set; }
    }
}
