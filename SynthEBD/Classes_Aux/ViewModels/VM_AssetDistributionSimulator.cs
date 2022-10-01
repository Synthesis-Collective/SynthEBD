using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_AssetDistributionSimulator : VM
    {
        public VM_AssetDistributionSimulator(VM_SettingsTexMesh texMesh, VM_SettingsBodyGen bodyGen, VM_SettingsOBody oBody, VM_BlockListUI blockListUI)
        {
            PrimaryAPs = texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary).Select(x => VM_AssetPack.DumpViewModelToModel(x)).ToHashSet();
            MixInAPs = texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn).Select(x => VM_AssetPack.DumpViewModelToModel(x)).ToHashSet();
            VM_SettingsOBody.DumpViewModelToModel(OBodySettings, oBody);
            VM_BlockListUI.DumpViewModelToModel(blockListUI, BlockList);
            VM_SettingsBodyGen.DumpViewModelToModel(bodyGen, new(), BodyGenConfigs);

            PatcherEnvironmentProvider.Instance.WhenAnyValue(x => x.Environment.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            this.WhenAnyValue(x => x.NPCformKey).Subscribe(x =>
            {
                if (lk.TryResolve<INpcGetter>(NPCformKey, out var npcGetter))
                {
                    NPCgetter = npcGetter;
                    NPCinfo = new NPCInfo(npcGetter, new(), new(), new(), new());
                }
            });

            SimulatePrimary = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SimulatePrimaryDistribution();
            }
        );
        }

        public ILinkCache lk { get; private set; }
        public IEnumerable<Type> NPCFormKeyTypes { get; set; } = typeof(INpcGetter).AsEnumerable();
        public FormKey NPCformKey { get; set; }
        public INpcGetter NPCgetter { get; set; }
        public NPCInfo NPCinfo { get; set; }
        public HashSet<AssetPack> PrimaryAPs { get; set; } = new();
        public HashSet<AssetPack> MixInAPs { get; set; } = new();
        public BodyGenConfigs BodyGenConfigs { get; set; } = new();
        public Settings_OBody OBodySettings { get; set; } = new();
        public BlockList BlockList { get; set; } = new();
        public int Repetitions { get; set; } = 100;
        public string TextReport { get; set; } = string.Empty;
        public RelayCommand SimulatePrimary { get; set; }

        public void SimulatePrimaryDistribution()
        {
            if (PrimaryAPs is null || !PrimaryAPs.Any()) { return; }
            if (NPCformKey.IsNull) { return; }

            var flattenedAssetPacks = PrimaryAPs.Select(x => FlattenedAssetPack.FlattenAssetPack(x)).ToHashSet();

            var blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(BlockList, NPCformKey);
            var blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(BlockList, NPCformKey);
            var blockBodyShape = false;
            if (blockListNPCEntry.BodyShape || blockListPluginEntry.BodyShape || !OBodyPreprocessing.NPCIsEligibleForBodySlide(NPCgetter)) { blockBodyShape = true; }

            HashSet<SubgroupCombination> combinations = new();

            for (int i = 0; i < Repetitions; i++)
            {
                var chosenCombination = AssetAndBodyShapeSelector.GenerateCombinationWithBodyShape(flattenedAssetPacks, BodyGenConfigs, OBodySettings, new AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment(), NPCinfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary, new());
                combinations.Add(chosenCombination);
            }

            GenerateReport(combinations);
        }

        public void GenerateReport(HashSet<SubgroupCombination> combinations)
        {
            List<CountableString> assetPacks = new();
            foreach (var combo in combinations)
            {
                var existing = assetPacks.Where(x => (x.Str) == combo.AssetPackName).FirstOrDefault();
                if (existing != null) { existing.Count++; }
                else
                {
                    assetPacks.Add(new() { Str = combo.AssetPackName });
                }
            }

            TextReport = "Asset Pack Assignment Counts:";
            foreach (var ap in assetPacks.OrderBy(x => x.Count))
            {
                TextReport += Environment.NewLine + ap.Str + " (" + ap.Count + ")";
            }
        }

        public class CountableString
        {
            public string Str { get; set; }
            public int Count { get; set; } = 0;
        }
    }
}
