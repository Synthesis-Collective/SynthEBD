using System.IO;
using Autofac;
using Newtonsoft.Json;
using Noggog;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verb that drafts a new asset-pack config from a working folder — the GUI Config Drafter run
/// headlessly. Replicates the drafter window's button sequence: categorize textures, dispose of
/// unmatched textures and duplicate groups per the command-line arguments, create a fresh
/// <see cref="VM_AssetPack"/>, import General race groupings/attribute groups, run
/// <see cref="ConfigDrafter.DraftConfigFromTextures"/>, apply the 3BA/BHUNP/TNG record-template set
/// when those texture families are detected, then dump and save the config JSON.
/// </summary>
public static class DraftVerb
{
    /// <summary>JSON/text report root for a draft run.</summary>
    public class DraftReport
    {
        public string Command { get; set; } = "draft";
        public string SavedTo { get; set; } = "";
        public string GroupName { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string DetectedGender { get; set; } = "";
        public string DefaultRecordTemplate { get; set; } = "";
        public bool HasEtcTextures { get; set; }
        public bool HasTNGTextures { get; set; }
        public string? AppliedTemplateSet { get; set; }
        public int TotalDdsFiles { get; set; }
        public int CategorizedCount { get; set; }
        public int UnmatchedKept { get; set; }
        public int UnmatchedIgnored { get; set; }
        public string MultipletMode { get; set; } = "";
        public int DuplicateGroups { get; set; }
        public List<TopLevelSubgroupSummary> TopLevelSubgroups { get; set; } = new();
    }

    /// <summary>Size summary of one top-level subgroup branch in the drafted config.</summary>
    public class TopLevelSubgroupSummary
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int TotalSubgroups { get; set; }
        public int TotalPaths { get; set; }
    }

    public static async Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        if (!IO_Aux.IsValidFilename(options.ConfigName))
        {
            Console.Error.WriteLine("--name must be usable as a file name; \"" + options.ConfigName + "\" is not.");
            return 2;
        }

        if (!DrafterCommon.ValidateRootLayout(options, out var layoutError))
        {
            Console.Error.WriteLine(layoutError);
            return 2;
        }

        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out var failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("Could not initialize the SynthEBD environment: " + failureReason);
            return 2;
        }

        using (bootstrapper)
        {
            // The drafter imports race groupings / attribute groups from the General settings VM, which a
            // headless bootstrap leaves unpopulated; fill it from the loaded model first.
            bootstrapper.PopulateGeneralSettingsViewModel();

            var configDrafter = bootstrapper.Container.Resolve<ConfigDrafter>();

            var (all, categorized, uncategorized) = DrafterCommon.CollectTextures(configDrafter, options);
            Console.Error.WriteLine("Found " + all.Count + " .dds file(s): " + categorized.Count + " categorized, "
                + uncategorized.Count + " unmatched.");
            if (!all.Any())
            {
                Console.Error.WriteLine("Nothing to draft.");
                return 2;
            }

            // Unmatched-texture disposition (the GUI presents these as checkboxes).
            var ignoredPaths = new List<string>();
            int unmatchedKept;
            if (!ResolveUnmatchedTextures(configDrafter, options, uncategorized, ignoredPaths, out unmatchedKept, out var unmatchedError))
            {
                Console.Error.WriteLine(unmatchedError);
                return 2;
            }

            // Duplicate-texture disposition (the GUI's Check for Duplicate Textures + Remove Duplicate Textures).
            var multipletDTOs = new List<Multiplet>();
            int duplicateGroupCount = 0;
            var multipletHandling = options.MultipletMode == CliMultipletMode.Ignore
                ? MultipletHandlingMode.Ignore
                : MultipletHandlingMode.Replace; // mode "none" drafts with an empty multiplet list, so Replace is a no-op
            if (options.MultipletMode != CliMultipletMode.None)
            {
                var duplicates = await DrafterCommon.ComputeDuplicatesAsync(configDrafter, options, all);
                duplicateGroupCount = duplicates.Count;
                if (!ApplyKeeperOverrides(duplicates, options, out var keeperError))
                {
                    Console.Error.WriteLine(keeperError);
                    return 2;
                }
                foreach (var group in duplicates)
                {
                    switch (options.MultipletMode)
                    {
                        case CliMultipletMode.Ignore:
                            group.ToIgnoreList(ignoredPaths);
                            break;
                        case CliMultipletMode.Replace:
                            multipletDTOs.Add(group.ToMultiplet());
                            break;
                    }
                }
            }
            else if (options.Keepers.Any())
            {
                Console.Error.WriteLine("--keeper has no effect with --multiplet-mode none.");
            }

            // Create a fresh config VM the same way the GUI's "Create New Config File" button does.
            var assetPackFactory = bootstrapper.Container.Resolve<VM_AssetPack.Factory>();
            var subgroupPlaceHolderFactory = bootstrapper.Container.Resolve<VM_SubgroupPlaceHolder.Factory>();
            var configVM = assetPackFactory(new AssetPack());
            var firstSubgroup = subgroupPlaceHolderFactory(new AssetPack.Subgroup { ID = "FS", Name = "First Subgroup" },
                null, configVM, configVM.Subgroups);
            configVM.Subgroups.Add(firstSubgroup);

            // The drafter window imports these from General settings when empty (a new config always is).
            configVM.RaceGroupingEditor.ImportFromGeneralSettings();
            configVM.AttributeGroupMenu.ImportFromGeneralSettings();

            Console.Error.WriteLine("Drafting...");
            var status = configDrafter.DraftConfigFromTextures(configVM, categorized, uncategorized, ignoredPaths,
                multipletDTOs, multipletHandling, options.Roots, options.RootsHavePrefix,
                options.AutoNames, options.AutoRules, options.AutoLinkage,
                out bool hasTNGTextures, out bool hasEtcTextures);

            if (status != configDrafter.SuccessString)
            {
                Console.Error.WriteLine("Error drafting config file: " + status);
                return 2;
            }

            configVM.GroupName = options.ConfigName!;
            configVM.ShortName = options.Prefix!;

            // Record-template handling for special texture families, mirroring VM_AssetPack.ImportTexturesButton.
            string? appliedTemplateSet = null;
            if (hasEtcTextures)
            {
                if (options.EtcBody == null)
                {
                    Console.Error.WriteLine("This mod contains \"etc\" body textures, which require body-specific record " +
                        "templates. Re-run with --etc-body 3BA or --etc-body BHUNP (ask the user which body their mod targets).");
                    return 2;
                }
                switch (options.EtcBody)
                {
                    case DrafterBodyType.CBBE_3BA:
                        configVM.ApplyCustomRecordTemplate("000801:Record Templates - 3BA - pamonha.esp", "000803:Record Templates - 3BA - pamonha.esp", "000805:Record Templates - 3BA - pamonha.esp", Gender.Female, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault);
                        appliedTemplateSet = "3BA";
                        break;
                    case DrafterBodyType.BHUNP:
                        configVM.ApplyCustomRecordTemplate("000801:Record Templates - BHUNP - pamonha.esp", "000803:Record Templates - BHUNP - pamonha.esp", "000805:Record Templates - BHUNP - pamonha.esp", Gender.Female, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault);
                        appliedTemplateSet = "BHUNP";
                        break;
                }
            }
            else if (hasTNGTextures)
            {
                configVM.ApplyCustomRecordTemplate("000800:Record Templates - The New Gentleman.esp", "000802:Record Templates - The New Gentleman.esp", "000804:Record Templates - The New Gentleman.esp", Gender.Male, VM_AdditionalRecordTemplate.AdditionalRacesPathsDefault.And(VM_AdditionalRecordTemplate.AdditionalRacesPathsTNG));
                appliedTemplateSet = "TNG";
            }

            var model = configVM.DumpViewModelToModel();

            string savedPath;
            bool saveSuccess;
            if (options.OutPath != null)
            {
                JSONhandler<AssetPack>.SaveJSONFile(model, options.OutPath, out saveSuccess, out string exceptionStr);
                if (!saveSuccess)
                {
                    Console.Error.WriteLine("Could not save drafted config: " + exceptionStr);
                }
                savedPath = options.OutPath;
            }
            else
            {
                var assetPackIO = bootstrapper.Container.Resolve<SettingsIO_AssetPack>();
                savedPath = assetPackIO.SaveAssetPack(model, out saveSuccess);
            }
            if (!saveSuccess)
            {
                return 2;
            }
            Console.Error.WriteLine("Drafted config saved to " + savedPath);

            var report = new DraftReport
            {
                SavedTo = savedPath,
                GroupName = model.GroupName,
                Prefix = model.ShortName,
                DetectedGender = model.Gender.ToString(),
                DefaultRecordTemplate = model.DefaultRecordTemplate.ToString(),
                HasEtcTextures = hasEtcTextures,
                HasTNGTextures = hasTNGTextures,
                AppliedTemplateSet = appliedTemplateSet,
                TotalDdsFiles = all.Count,
                CategorizedCount = categorized.Count,
                UnmatchedKept = unmatchedKept,
                UnmatchedIgnored = uncategorized.Count - unmatchedKept,
                MultipletMode = options.MultipletMode.ToString(),
                DuplicateGroups = duplicateGroupCount,
                TopLevelSubgroups = SummarizeTopLevels(model),
            };

            if (options.Json)
            {
                resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            else
            {
                WriteTextReport(report, resultWriter);
            }

            return 0;
        }
    }

    /// <summary>
    /// Applies the --keep-unmatched / --keep-all-unmatched / --ignore-all-unmatched disposition: kept
    /// textures stay in the uncategorized list (becoming Unknown-type subgroups); the rest are added to
    /// <paramref name="ignoredPaths"/> in root-relative form, as the GUI does. Fails when unmatched
    /// textures exist but no disposition was specified, or a --keep-unmatched path matches nothing.
    /// </summary>
    private static bool ResolveUnmatchedTextures(ConfigDrafter configDrafter, CliOptions options,
        List<string> uncategorized, List<string> ignoredPaths, out int keptCount, out string error)
    {
        error = string.Empty;
        keptCount = 0;
        if (!uncategorized.Any())
        {
            return true;
        }

        var trimmedByFull = uncategorized.ToDictionary(path => path,
            path => DrafterCommon.Trim(configDrafter, options, path));

        if (options.KeepAllUnmatched)
        {
            keptCount = uncategorized.Count;
            return true;
        }

        if (!options.IgnoreAllUnmatched && !options.KeepUnmatched.Any())
        {
            error = "This mod contains " + uncategorized.Count + " texture(s) the drafter does not recognize. " +
                "Decide which (if any) to keep, then re-run with --keep-unmatched <path>, --keep-all-unmatched, or " +
                "--ignore-all-unmatched. Unmatched textures:" + Environment.NewLine +
                string.Join(Environment.NewLine, trimmedByFull.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            return false;
        }

        var keepSet = new HashSet<string>(options.KeepUnmatched, StringComparer.OrdinalIgnoreCase);
        var matchedKeeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fullPath, trimmedPath) in trimmedByFull)
        {
            if (keepSet.Contains(trimmedPath) || keepSet.Contains(fullPath))
            {
                keptCount++;
                matchedKeeps.Add(keepSet.Contains(trimmedPath) ? trimmedPath : fullPath);
            }
            else
            {
                ignoredPaths.Add(trimmedPath);
            }
        }

        var unmatchedKeeps = keepSet.Where(x => !matchedKeeps.Contains(x)).ToList();
        if (unmatchedKeeps.Any())
        {
            error = "--keep-unmatched path(s) did not match any unmatched texture: " +
                string.Join(", ", unmatchedKeeps);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Re-points duplicate groups at user-chosen keepers (--keeper) and verifies every group ends up
    /// with exactly one keeper, mirroring the GUI's pre-removal check.
    /// </summary>
    private static bool ApplyKeeperOverrides(List<VM_FileDuplicateContainer> duplicates, CliOptions options, out string error)
    {
        error = string.Empty;
        foreach (var keeperPath in options.Keepers)
        {
            var group = duplicates.FirstOrDefault(g => g.FilePaths.Any(f =>
                f.DisplayedPath.Equals(keeperPath, StringComparison.OrdinalIgnoreCase)
                || f.FullPath.Equals(keeperPath, StringComparison.OrdinalIgnoreCase)));
            if (group == null)
            {
                error = "--keeper path is not part of any duplicate group: " + keeperPath;
                return false;
            }
            foreach (var file in group.FilePaths)
            {
                file.IsSelected = !(file.DisplayedPath.Equals(keeperPath, StringComparison.OrdinalIgnoreCase)
                    || file.FullPath.Equals(keeperPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        var malformed = duplicates.Where(g => g.FilePaths.Count(f => !f.IsSelected) != 1).Select(g => g.FileName).ToList();
        if (malformed.Any())
        {
            error = "Duplicate group(s) without exactly one keeper: " + string.Join(", ", malformed);
            return false;
        }
        return true;
    }

    /// <summary>Counts subgroups and asset paths per top-level branch of the drafted model.</summary>
    private static List<TopLevelSubgroupSummary> SummarizeTopLevels(AssetPack model)
    {
        var summaries = new List<TopLevelSubgroupSummary>();
        foreach (var topLevel in model.Subgroups)
        {
            int subgroupCount = 0;
            int pathCount = 0;
            void Count(AssetPack.Subgroup subgroup)
            {
                subgroupCount++;
                pathCount += subgroup.Paths.Count;
                foreach (var child in subgroup.Subgroups)
                {
                    Count(child);
                }
            }
            Count(topLevel);
            summaries.Add(new TopLevelSubgroupSummary
            {
                Id = topLevel.ID,
                Name = topLevel.Name,
                TotalSubgroups = subgroupCount,
                TotalPaths = pathCount,
            });
        }
        return summaries;
    }

    private static void WriteTextReport(DraftReport report, TextWriter writer)
    {
        writer.WriteLine("Drafted \"" + report.GroupName + "\" (prefix " + report.Prefix + ", gender " + report.DetectedGender + ")");
        writer.WriteLine("Saved to: " + report.SavedTo);
        writer.WriteLine("Textures: " + report.TotalDdsFiles + " found, " + report.CategorizedCount + " categorized, "
            + report.UnmatchedKept + " unmatched kept, " + report.UnmatchedIgnored + " unmatched ignored.");
        writer.WriteLine("Duplicates: " + report.DuplicateGroups + " group(s), mode " + report.MultipletMode + ".");
        if (report.AppliedTemplateSet != null)
        {
            writer.WriteLine("Applied record-template set: " + report.AppliedTemplateSet);
        }
        writer.WriteLine("Default record template: " + report.DefaultRecordTemplate);
        writer.WriteLine("Top-level subgroups:");
        foreach (var topLevel in report.TopLevelSubgroups)
        {
            writer.WriteLine("  " + topLevel.Id + ": " + topLevel.Name + " (" + topLevel.TotalSubgroups
                + " subgroup(s), " + topLevel.TotalPaths + " path(s))");
        }
    }
}
