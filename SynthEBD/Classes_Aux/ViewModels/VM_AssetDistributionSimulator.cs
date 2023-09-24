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
        private readonly IEnvironmentStateProvider _environmentProvider;
        private readonly PatcherState _patcherState;
        private readonly Logger _logger;
        private readonly SynthEBDPaths _paths;
        private readonly DictionaryMapper _dictionaryMapper;
        private readonly AssetAndBodyShapeSelector _assetAndBodyShapeSelector;
        private readonly AssetSelector _assetSelector;
        private readonly SettingsIO_OBody _oBodyIO;
        private readonly OBodyPreprocessing _obodyPreProcessing;
        private readonly NPCInfo.Factory _npcInfoFactory;
        private readonly VM_SettingsTexMesh _texMesh;
        public delegate VM_AssetDistributionSimulator Factory();
        public VM_AssetDistributionSimulator(VM_SettingsTexMesh texMesh, VM_SettingsBodyGen bodyGen, VM_SettingsOBody oBody, VM_BlockListUI blockListUI, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, DictionaryMapper dictionaryMapper, AssetAndBodyShapeSelector assetAndBodyShapeSelector, AssetSelector assetSelector, OBodyPreprocessing oBodyPreprocessing, SettingsIO_OBody oBodyIO, NPCInfo.Factory npcInfoFactory)
        {
            _environmentProvider = environmentProvider;
            _patcherState = patcherState;
            _logger = logger;
            _paths = paths;
            _dictionaryMapper = dictionaryMapper;
            _assetAndBodyShapeSelector = assetAndBodyShapeSelector;
            _assetSelector = assetSelector;
            _obodyPreProcessing = oBodyPreprocessing;
            _oBodyIO = oBodyIO;
            _npcInfoFactory = npcInfoFactory;
            _texMesh = texMesh;

            OBodySettings = oBody.DumpViewModelToModel();
            BlockList = blockListUI.DumpViewModelToModel();
            BodyGenConfigs = bodyGen.DumpBodyGenConfigsToModels();

            _environmentProvider.WhenAnyValue(x => x.LinkCache)
                .Subscribe(x => lk = x)
                .DisposeWith(this);

            this.WhenAnyValue(x => x.NPCformKey).Subscribe(x =>
            {
                if (lk.TryResolve<INpcGetter>(NPCformKey, out var npcGetter))
                {
                    NPCgetter = npcGetter;
                    ShowFullReportVisible = false;
                }
                TextReport = String.Empty;
                AssetReports.Clear();
            }).DisposeWith(this); ;

            SimulatePrimary = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SimulatePrimaryDistribution();
                ShowFullReportVisible = true;
            });

            ShowFullReport = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                DislpayFullReportPopup(NPCinfo);
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
        public Settings_OBody OBodySettings { get; set; }
        public BlockList BlockList { get; set; } = new();
        public int Repetitions { get; set; } = 100;
        public string TextReport { get; set; } = string.Empty;
        public ObservableCollection<AssetReport> AssetReports { get; set; } = new();
        public RelayCommand SimulatePrimary { get; set; }
        public RelayCommand ShowFullReport { get; set; }
        public bool ShowFullReportVisible { get; set; } = false;

        public void Reinitialize()
        {
            PrimaryAPs = _texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary && x.IsSelected).Select(x => x.DumpViewModelToModel()).ToHashSet();
            MixInAPs = _texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn && x.IsSelected).Select(x => x.DumpViewModelToModel()).ToHashSet();
            NPCformKey = new();
            Clear();
        }

        private void Clear()
        {
            AssetReports.Clear();
            TextReport = string.Empty;
            ShowFullReportVisible = false;
        }
        public void SimulatePrimaryDistribution()
        {
            Clear();
            if (PrimaryAPs is null || !PrimaryAPs.Any()) { return; }
            if (NPCformKey.IsNull) { return; }

            NPCinfo = _npcInfoFactory(NPCgetter, new(), new());

            var flattenedAssetPacks = PrimaryAPs.Where(x => x.Gender == NPCinfo.Gender).Select(x => FlattenedAssetPack.FlattenAssetPack(x, _dictionaryMapper, _patcherState)).ToHashSet();

            var blockListNPCEntry = BlockListHandler.GetCurrentNPCBlockStatus(BlockList, NPCformKey);
            var blockListPluginEntry = BlockListHandler.GetCurrentPluginBlockStatus(BlockList, NPCformKey, _environmentProvider.LinkCache);
            var blockBodyShape = false;
            if (blockListNPCEntry.BodyShape || blockListPluginEntry.BodyShape || !_obodyPreProcessing.NPCIsEligibleForBodySlide(NPCgetter)) { blockBodyShape = true; }

            HashSet<SubgroupCombination> combinations = new();

            var currentDetailedVerboseSetting = _patcherState.GeneralSettings.VerboseModeDetailedAttributes;

            bool backupConsistency = _patcherState.GeneralSettings.bEnableConsistency;
            _patcherState.GeneralSettings.bEnableConsistency = false;

            for (int i = 0; i < Repetitions; i++)
            {
                if (i == Repetitions - 1)
                {
                    _patcherState.GeneralSettings.VerboseModeDetailedAttributes = true;
                    NPCinfo.Report.LogCurrentNPC = true;
                    _logger.InitializeNewReport(NPCinfo);
                }
                if (!blockBodyShape)
                {
                    var assignments = _assetAndBodyShapeSelector.GenerateCombinationWithBodyShape(flattenedAssetPacks, BodyGenConfigs, OBodySettings, NPCinfo, AssetSelector.AssetPackAssignmentMode.Primary, new());
                    if (assignments.Assets != null)
                    {
                        combinations.Add(assignments.Assets);
                    }
                }
                else
                {
                    var assignments = _assetSelector.AssignAssets(NPCinfo, AssetSelector.AssetPackAssignmentMode.Primary, flattenedAssetPacks, null, null, out _);
                    if (assignments != null)
                    {
                        combinations.Add(assignments);
                    }
                }
                if (i == Repetitions - 1)
                {
                    NPCinfo.Report.LogCurrentNPC = false;
                    _patcherState.GeneralSettings.VerboseModeDetailedAttributes = currentDetailedVerboseSetting;
                }
            }

            _patcherState.GeneralSettings.bEnableConsistency = backupConsistency;

            GenerateReport(combinations, flattenedAssetPacks, NPCinfo);
        }

        public void GenerateReport(HashSet<SubgroupCombination> combinations, HashSet<FlattenedAssetPack> available, NPCInfo npcInfo)
        {
            List<CountableString> assetPacks = new();
            foreach (var combo in combinations.Where(x => x.AssetPack != null).ToArray())
            {
                var existing = assetPacks.Where(x => x.Str == combo.AssetPackName).FirstOrDefault();
                if (existing != null) { existing.Count++; }
                else
                {
                    assetPacks.Add(new() { Str = combo.AssetPackName });
                }
            }

            var candidatePacks = available.Where(x => !assetPacks.Select(x => x.Str).Contains(x.GroupName)).ToArray();
            foreach (var assetPack in candidatePacks)
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
                        if (sgString.Count > 0) { reportString.TextColor = CommonColors.White; }
                        else { reportString.TextColor = CommonColors.FireBrick; }
                        reportString.GetExplainStringSubgroup(npcInfo, ap.GroupName, "Subgroup " + subgroup.GetDetailedID_NameString(false));
                        assetReport.SubgroupStrings.Add(reportString);
                    }
                }
                AssetReports.Add(assetReport);
            }
        }

        public void DislpayFullReportPopup(NPCInfo npcInfo)
        {
            var fullReport = npcInfo.Report;

            if (fullReport == null)
            {
                return;
            }

            System.Xml.Linq.XDocument output = new();
            output.Add(npcInfo.Report.RootElement);

            var outputStr = Logger.FormatLogStringIndents(output.ToString());
            CustomMessageBox.DisplayNotificationOK("Copy this to a text editor: Notepad++ is recommended", outputStr);
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
            public SolidColorBrush TextColor { get; set; } = CommonColors.White;
            public RelayCommand ExplainCommand { get; }
            public string ExplainStr { get; set; }

            public void GetExplainStringSubgroup(NPCInfo npcInfo, string assetPackName, string reportIDstring)
            {
                var log = npcInfo.Report.RootElement.ToString();

                string startStr = "Filtering subgroups within asset pack: " + assetPackName;
                var split1 = log.Split(startStr).ToArray();
                if (split1.Length < 2) { ExplainStr = "Could not parse the log."; return; }

                string endStr = "</AssetPack>";
                var split2 = split1[1].Split(endStr);
                if (split2.Length < 2) { ExplainStr = "Could not parse the log."; return; }

                string subgroupStrs = split2[0];

                var subgroupStrArray = subgroupStrs.Split(Environment.NewLine).Where(x => !x.IsNullOrWhitespace()).ToArray();
                ExplainStr = subgroupStrArray.Where(x => ReplaceWhitespace(x, string.Empty).StartsWith(ReplaceWhitespace(reportIDstring, string.Empty))).FirstOrDefault() ?? "No relevant information found";
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
