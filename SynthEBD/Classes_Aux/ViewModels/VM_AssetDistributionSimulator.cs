using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_AssetDistributionSimulator : VM
    {
        public VM_AssetDistributionSimulator(VM_SettingsTexMesh texMesh, VM_SettingsBodyGen bodyGen, VM_SettingsOBody oBody, VM_BlockListUI blockListUI)
        {
            PrimaryAPs = texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary && x.IsSelected).Select(x => VM_AssetPack.DumpViewModelToModel(x)).ToHashSet();
            MixInAPs = texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn && x.IsSelected).Select(x => VM_AssetPack.DumpViewModelToModel(x)).ToHashSet();
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
                    NPCinfo = new NPCInfo(npcGetter, new(), new(), new(), new(), new());
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
        public ObservableCollection<AssetReport> AssetReports { get; set; } = new();
        public RelayCommand SimulatePrimary { get; set; }

        public void SimulatePrimaryDistribution()
        {
            if (PrimaryAPs is null || !PrimaryAPs.Any()) { return; }
            if (NPCformKey.IsNull) { return; }

            var flattenedAssetPacks = PrimaryAPs.Where(x => x.Gender == NPCinfo.Gender).Select(x => FlattenedAssetPack.FlattenAssetPack(x)).ToHashSet();

            var blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(BlockList, NPCformKey);
            var blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(BlockList, NPCformKey);
            var blockBodyShape = false;
            if (blockListNPCEntry.BodyShape || blockListPluginEntry.BodyShape || !OBodyPreprocessing.NPCIsEligibleForBodySlide(NPCgetter)) { blockBodyShape = true; }

            HashSet<SubgroupCombination> combinations = new();

            var currentDetailedVerboseSetting = PatcherSettings.General.VerboseModeDetailedAttributes;

            for (int i = 0; i < Repetitions; i++)
            {
                if (i == Repetitions - 1)
                {
                    PatcherSettings.General.VerboseModeDetailedAttributes = true;
                    NPCinfo.Report.LogCurrentNPC = true;
                    Logger.InitializeNewReport(NPCinfo);
                }
                var chosenCombination = AssetAndBodyShapeSelector.GenerateCombinationWithBodyShape(flattenedAssetPacks, BodyGenConfigs, OBodySettings, new AssetAndBodyShapeSelector.AssetAndBodyShapeAssignment(), NPCinfo, blockBodyShape, AssetAndBodyShapeSelector.AssetPackAssignmentMode.Primary, new());
                combinations.Add(chosenCombination);
                if (i == Repetitions - 1)
                {
                    NPCinfo.Report.LogCurrentNPC = false;
                    PatcherSettings.General.VerboseModeDetailedAttributes = currentDetailedVerboseSetting;
                }
            }

            GenerateReport(combinations, flattenedAssetPacks, NPCinfo);
        }

        public void GenerateReport(HashSet<SubgroupCombination> combinations, HashSet<FlattenedAssetPack> available, NPCInfo npcInfo)
        {
            List<CountableString> assetPacks = new();
            foreach (var combo in combinations.Where(x => x.AssetPack != null))
            {
                var existing = assetPacks.Where(x => (x.Str) == combo.AssetPackName).FirstOrDefault();
                if (existing != null) { existing.Count++; }
                else
                {
                    assetPacks.Add(new() { Str = combo.AssetPackName });
                }
            }

            foreach (var assetPack in available.Where(x => !assetPacks.Select(x => x.Str).Contains(x.GroupName)))
            {
                assetPacks.Add(new() {  Str = assetPack.GroupName, Count = 0 });
            }

            TextReport = "Asset Pack Assignment Counts:";
            foreach (var ap in assetPacks.OrderBy(x => x.Count))
            {
                TextReport += Environment.NewLine + ap.Str + " (" + ap.Count + ")";
            }

            TextReport += Environment.NewLine + Environment.NewLine + "Subgroup Assignment Counts:";
            foreach (var ap in available)
            {
                if (!assetPacks.Where(x => x.Str == ap.GroupName).Any()) { continue; }
                AssetReport assetReport = new();
                assetReport.TitleString += Environment.NewLine + "====================" + Environment.NewLine + ap.GroupName + Environment.NewLine + "====================" + Environment.NewLine;
                List<CountableString> subgroups = new();
                assetReport.SubgroupStrings = new();
                for (int i = 0; i < ap.Subgroups.Count; i++)
                {
                    var index = ap.Subgroups[i];
                    foreach (var subgroup in index)
                    {
                        CountableString sgString = new() { Str = subgroup.Id + " (" + subgroup.Name + "): " };
                        sgString.Count = combinations.Where(x => x.AssetPackName == ap.GroupName && x.ContainedSubgroups[i].Id == subgroup.Id).Count();

                        var reportString = new VM_ReportCountableStringWrapper(sgString);
                        if (sgString.Count > 0) { reportString.TextColor = new SolidColorBrush(Colors.White); }
                        else {  reportString.TextColor = new SolidColorBrush(Colors.Firebrick); }
                        reportString.GetExplainStringSubgroup(npcInfo, ap.GroupName, subgroup.Id, subgroup.Name);
                        assetReport.SubgroupStrings.Add(reportString);
                    }
                }
                AssetReports.Add(assetReport);
            }
        }
        
        public class AssetReport
        {
            public string TitleString { get; set; } = "";
            public ObservableCollection<VM_ReportCountableStringWrapper> SubgroupStrings { get; set; } = new();
        }

        public class CountableString
        {
            public string Str { get; set; }
            public int Count { get; set; } = 1;
        }

        public class VM_ReportCountableStringWrapper
        {
            public VM_ReportCountableStringWrapper(CountableString str)
            {
                ReferencedStr = str;
                ExplainCommand = new RelayCommand(canExecute: _ => true, execute: _ =>
                {
                    CustomMessageBox.DisplayNotificationOK("Explanation", ExplainStr);
                });
            }

            public CountableString ReferencedStr { get; set; }
            public SolidColorBrush TextColor { get; set; } = new SolidColorBrush(Colors.White);
            public RelayCommand ExplainCommand { get; }
            public string ExplainStr { get; set; }

            public void GetExplainStringSubgroup(NPCInfo npcInfo, string assetPackName, string subGroupID, string subgroupName)
            {
                var log = npcInfo.Report.RootElement.ToString();

                string startStr = "Filtering subgroups within asset pack: " + assetPackName;
                var split1 = log.Split(startStr);
                if (split1.Length < 2) { ExplainStr = "Could not parse the log."; return; }

                string endStr = "</AssetPack>";
                var split2 = split1[1].Split(endStr);
                if (split2.Length < 2) { ExplainStr = "Could not parse the log."; return; }

                string subgroupStrs = split2[0];

                var subgroupStrArray = subgroupStrs.Split(Environment.NewLine).Where(x => !x.IsNullOrWhitespace());
                string matchStr = "Subgroup" + subGroupID + "(" + subgroupName + ")"; // remove all white space to remain agnostic to formatting
                ExplainStr = subgroupStrArray.Where(x => ReplaceWhitespace(x, string.Empty).StartsWith(ReplaceWhitespace(matchStr, string.Empty))).FirstOrDefault() ?? "No relevant information found";
            }

            //https://stackoverflow.com/questions/6219454/efficient-way-to-remove-all-whitespace-from-string
            private static readonly Regex sWhitespace = new Regex(@"\s+");
            public static string ReplaceWhitespace(string input, string replacement)
            {
                return sWhitespace.Replace(input, replacement);
            }
        }
    }
}
