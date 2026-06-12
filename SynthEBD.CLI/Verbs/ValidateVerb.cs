using System.IO;
using Autofac;
using Newtonsoft.Json;

namespace SynthEBD.CLI;

/// <summary>
/// CLI verb that runs <see cref="AssetPackValidator"/> against installed asset-pack configs — the same
/// checks as the GUI's per-config Validate button — and reports the results to stdout (text or JSON).
/// Extraction working folders passed via <c>--asset-root</c> are forwarded to
/// <see cref="AssetPackValidator.ExtraAssetRoots"/> so Source files can be validated before the assets are
/// visible to the game or a mod manager.
/// </summary>
public static class ValidateVerb
{
    /// <summary>JSON/text report root for a validate run.</summary>
    public class ValidationReport
    {
        public string Command { get; set; } = "validate";
        public bool AllValid { get; set; }
        /// <summary>Active body-shape mode; descriptor checks only run for the active mode's descriptor type.</summary>
        public string BodySelectionMode { get; set; } = "";
        public string DataFolder { get; set; } = "";
        public List<string> ExtraAssetRoots { get; set; } = new();
        public List<ConfigValidationResult> Configs { get; set; } = new();
    }

    /// <summary>Validation outcome for one asset-pack config.</summary>
    public class ConfigValidationResult
    {
        public string Name { get; set; } = "";
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public static Task<int> RunAsync(CliOptions options, TextWriter resultWriter)
    {
        if (!CliBootstrapper.TryCreate(options, out var bootstrapper, out var failureReason) || bootstrapper == null)
        {
            Console.Error.WriteLine("Could not initialize the SynthEBD environment: " + failureReason);
            return Task.FromResult(2);
        }

        using (bootstrapper)
        {
            var state = bootstrapper.PatcherState;

            List<AssetPack> targets;
            if (options.ConfigNames.Any())
            {
                targets = new();
                var notFound = new List<string>();
                foreach (var name in options.ConfigNames)
                {
                    var match = state.AssetPacks.FirstOrDefault(x => MatchesConfigName(x, name));
                    if (match != null) { targets.Add(match); }
                    else { notFound.Add(name); }
                }
                if (notFound.Any())
                {
                    Console.Error.WriteLine("Config(s) not found among installed asset packs: " + string.Join(", ", notFound));
                    Console.Error.WriteLine("Installed configs: " + string.Join(", ", state.AssetPacks.Select(x => x.GroupName)));
                    return Task.FromResult(2);
                }
            }
            else
            {
                targets = state.AssetPacks.ToList();
            }

            var validator = bootstrapper.Container.Resolve<AssetPackValidator>();
            validator.ExtraAssetRoots.Clear();
            validator.ExtraAssetRoots.AddRange(options.AssetRoots);

            var report = new ValidationReport
            {
                BodySelectionMode = state.GeneralSettings.BodySelectionMode.ToString(),
                DataFolder = bootstrapper.EnvironmentProvider.DataFolderPath.ToString(),
                ExtraAssetRoots = options.AssetRoots.ToList(),
            };

            foreach (var assetPack in targets)
            {
                var errors = new List<string>();
                bool isValid = validator.Validate(assetPack, errors, state.BodyGenConfigs, state.OBodySettings);
                report.Configs.Add(new ConfigValidationResult
                {
                    Name = assetPack.GroupName,
                    IsValid = isValid,
                    // The validator appends bare newline entries as visual separators; drop them.
                    Errors = errors.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
                });
            }

            report.AllValid = report.Configs.All(x => x.IsValid);

            if (options.Json)
            {
                resultWriter.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            else
            {
                WriteTextReport(report, resultWriter);
            }

            return Task.FromResult(report.AllValid ? 0 : 1);
        }
    }

    /// <summary>Matches a config by GroupName or by its file name without extension (case-insensitive).</summary>
    private static bool MatchesConfigName(AssetPack assetPack, string name)
    {
        if (assetPack.GroupName != null && assetPack.GroupName.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return assetPack.FilePath != null
            && Path.GetFileNameWithoutExtension(assetPack.FilePath).Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteTextReport(ValidationReport report, TextWriter writer)
    {
        writer.WriteLine("Body selection mode: " + report.BodySelectionMode);
        foreach (var config in report.Configs)
        {
            writer.WriteLine();
            writer.WriteLine((config.IsValid ? "VALID:   " : "INVALID: ") + config.Name);
            foreach (var error in config.Errors)
            {
                writer.WriteLine("  " + error);
            }
        }
        writer.WriteLine();
        writer.WriteLine(report.AllValid
            ? "All " + report.Configs.Count + " config(s) passed validation."
            : report.Configs.Count(x => !x.IsValid) + " of " + report.Configs.Count + " config(s) failed validation.");
    }
}
