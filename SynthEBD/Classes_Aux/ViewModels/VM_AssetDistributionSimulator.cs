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
    /// <summary>
    /// View model for the asset-distribution simulator: repeatedly runs the asset/body-shape selection
    /// pipeline for one NPC to estimate how often each asset pack and subgroup would be assigned, and
    /// produces a per-subgroup count report (with per-entry explanations drawn from the verbose log).
    /// </summary>
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
        private readonly AssetDistributionSimulator _simulator;
        /// <summary>Autofac factory delegate for constructing the simulator.</summary>
        public delegate VM_AssetDistributionSimulator Factory();
        /// <summary>Creates the simulator, snapshotting the OBody / BlockList / BodyGen settings and wiring the simulate and show-full-report commands.</summary>
        public VM_AssetDistributionSimulator(VM_SettingsTexMesh texMesh, VM_SettingsBodyGen bodyGen, VM_SettingsOBody oBody, VM_BlockListUI blockListUI, IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths, DictionaryMapper dictionaryMapper, AssetAndBodyShapeSelector assetAndBodyShapeSelector, AssetSelector assetSelector, OBodyPreprocessing oBodyPreprocessing, SettingsIO_OBody oBodyIO, NPCInfo.Factory npcInfoFactory, AssetDistributionSimulator simulator)
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
            _simulator = simulator;

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
                if (SimulatePrimaryDistribution())
                {
                    ShowFullReportVisible = true;
                }
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

        /// <summary>Refreshes the snapshot of selected primary/mix-in asset packs and resets the NPC and report.</summary>
        public void Reinitialize()
        {
            PrimaryAPs = _texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.Primary && x.IsSelected).Select(x => x.DumpViewModelToModel()).ToHashSet();
            MixInAPs = _texMesh.AssetPacks.Where(x => x.ConfigType == AssetPackType.MixIn && x.IsSelected).Select(x => x.DumpViewModelToModel()).ToHashSet();
            NPCformKey = new();
            Clear();
        }

        /// <summary>Clears the current report state.</summary>
        private void Clear()
        {
            AssetReports.Clear();
            TextReport = string.Empty;
            ShowFullReportVisible = false;
        }
        /// <summary>Runs primary-asset selection <see cref="Repetitions"/> times for the selected NPC via
        /// <see cref="AssetDistributionSimulator"/>, then builds the count report.</summary>
        /// <returns><c>true</c> if the simulation ran; <c>false</c> with a notification on invalid input.</returns>
        public bool SimulatePrimaryDistribution()
        {
            Clear();
            if (PrimaryAPs is null || !PrimaryAPs.Any()) {
                MessageWindow.DisplayNotificationOK("Cannot Simulate Distribution", "No Primary Config Files are selected in the Active Config Files list");
                return false;
            }
            if (NPCformKey.IsNull) {
                MessageWindow.DisplayNotificationOK("Cannot Simulate Distribution", "No NPC is selected");
                return false;
            }

            var result = _simulator.SimulatePrimaryDistribution(NPCgetter, PrimaryAPs, BodyGenConfigs, OBodySettings, BlockList, Repetitions, out string failureReason);
            if (result == null)
            {
                MessageWindow.DisplayNotificationOK("Cannot Simulate Distribution", failureReason);
                return false;
            }

            NPCinfo = result.NPCInfo;
            GenerateReport(result.Combinations, result.AvailableAssetPacks, result.NPCInfo);
            return true;
        }

        /// <summary>Tallies how often each asset pack and subgroup appeared across the simulated combinations and builds the text and per-subgroup reports (coloring zero-count subgroups red).</summary>
        /// <param name="combinations">The simulated subgroup combinations.</param>
        /// <param name="available">The flattened asset packs that were eligible.</param>
        /// <param name="npcInfo">The simulated NPC (source of the verbose log for explanations).</param>
        public void GenerateReport(HashSet<SubgroupCombination> combinations, HashSet<FlattenedAssetPack> available, NPCInfo npcInfo)
        {
            var counts = _simulator.Tally(combinations, available);

            TextReport = "Asset Pack Assignment Counts:";
            foreach (var ap in counts.AssetPackCounts)
            {
                TextReport += Environment.NewLine + ap.Name + " (" + ap.Count + ")";
            }

            TextReport += Environment.NewLine + Environment.NewLine + "Subgroup Assignment Counts:";
            foreach (var packCounts in counts.SubgroupCounts)
            {
                AssetReport assetReport = new();
                assetReport.TitleString += Environment.NewLine + "====================" + Environment.NewLine + packCounts.GroupName + Environment.NewLine + "====================" + Environment.NewLine;
                assetReport.SubgroupStrings = new();
                foreach (var subgroup in packCounts.Subgroups)
                {
                    CountableString sgString = new() { Str = subgroup.Id + " (" + subgroup.Name + "): ", Count = subgroup.Count };

                    var reportString = new VM_ReportCountableStringWrapper(sgString);
                    if (sgString.Count > 0) { reportString.TextColor = CommonColors.White; }
                    else { reportString.TextColor = CommonColors.FireBrick; }
                    reportString.GetExplainStringSubgroup(npcInfo, packCounts.GroupName, subgroup.DetailedIdName);
                    assetReport.SubgroupStrings.Add(reportString);
                }
                AssetReports.Add(assetReport);
            }
        }

        /// <summary>Shows the full verbose NPC report (formatted XML) in a copyable popup.</summary>
        /// <param name="npcInfo">The NPC whose report to display.</param>
        public void DislpayFullReportPopup(NPCInfo npcInfo)
        {
            var outputStr = AssetDistributionSimulator.FormatFullReport(npcInfo);
            if (outputStr.IsNullOrWhitespace())
            {
                return;
            }
            MessageWindow.DisplayNotificationOK("Copy this to a text editor: Notepad++ is recommended", outputStr);
        }


        /// <summary>One asset pack's section of the distribution report: a title and per-subgroup count rows.</summary>
        public class AssetReport
        {
            public string TitleString { get; set; } = "";
            public ObservableCollection<VM_ReportCountableStringWrapper> SubgroupStrings { get; set; } = new();
        }

        /// <summary>A string paired with an occurrence count.</summary>
        public class CountableString
        {
            public string Str { get; set; }
            public int Count { get; set; } = 1;
        }

        /// <summary>Wraps a <see cref="CountableString"/> for display with a color and an "explain" command that surfaces the relevant verbose-log excerpt.</summary>
        public class VM_ReportCountableStringWrapper
        {
            /// <summary>Creates the wrapper and wires the explain command.</summary>
            /// <param name="str">The countable string to wrap.</param>
            public VM_ReportCountableStringWrapper(CountableString str)
            {
                ReferencedStr = str;
                ExplainCommand = new RelayCommand(canExecute: _ => true, execute: _ =>
                {
                    MessageWindow.DisplayNotificationOK("Explanation", ExplainStr);
                });
            }

            public CountableString ReferencedStr { get; set; }
            public SolidColorBrush TextColor { get; set; } = CommonColors.White;
            public RelayCommand ExplainCommand { get; }
            public string ExplainStr { get; set; }

            /// <summary>Extracts the verbose-log lines explaining this subgroup's filtering (for the given NPC and asset pack) into <see cref="ExplainStr"/>, matching whitespace-insensitively.</summary>
            /// <param name="npcInfo">The simulated NPC (source of the report log).</param>
            /// <param name="assetPackName">The asset pack whose log section is searched.</param>
            /// <param name="reportIDstring">The subgroup identifier to find in the log.</param>
            public void GetExplainStringSubgroup(NPCInfo npcInfo, string assetPackName, string reportIDstring)
            {
                ExplainStr = AssetDistributionSimulator.ExtractSubgroupExplanation(npcInfo, assetPackName, reportIDstring);
            }

            /// <summary>Replaces all whitespace runs in a string (used for tolerant log-line matching).</summary>
            public static string ReplaceWhitespace(string input, string replacement)
            {
                return AssetDistributionSimulator.ReplaceWhitespace(input, replacement);
            }
        }
    }
}
