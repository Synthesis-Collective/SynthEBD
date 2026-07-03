using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// Guards the 3-part tooltip system: every DocTooltip.Key literal referenced in the migrated XAML
/// files must resolve to a registered <see cref="UiDocs"/> entry (the app only Debug.Asserts at
/// runtime, which automated Release runs would miss), and every registered entry must carry all
/// three documentation sections.
/// </summary>
public class UiDocsTests
{
    private static readonly Regex DocKeyLiteral = new("DocTooltip\\.Key=\"(?<key>[^\"{][^\"]*)\"", RegexOptions.Compiled);

    [Fact]
    public void AllEntries_HaveAllThreeSections()
    {
        UiDocs.All.Should().NotBeEmpty();
        foreach (var (key, entry) in UiDocs.All)
        {
            entry.Layperson.Should().NotBeNullOrWhiteSpace("entry \"{0}\" needs a Layperson section", key);
            entry.Technical.Should().NotBeNullOrWhiteSpace("entry \"{0}\" needs a Technical section", key);
            entry.Motivation.Should().NotBeNullOrWhiteSpace("entry \"{0}\" needs a Motivation section", key);
        }
    }

    [Fact]
    public void AllXamlDocKeys_ResolveToRegisteredEntries()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            return; // Source tree not available (e.g. isolated CI artifact run); the runtime Debug.Assert still covers dev runs.
        }

        var xamlFiles = Directory.EnumerateFiles(Path.Combine(repoRoot, "SynthEBD"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));

        var missing = new List<string>();
        foreach (var file in xamlFiles)
        {
            foreach (Match match in DocKeyLiteral.Matches(File.ReadAllText(file)))
            {
                var key = match.Groups["key"].Value;
                if (!UiDocs.All.ContainsKey(key))
                {
                    missing.Add(key + " (" + Path.GetFileName(file) + ")");
                }
            }
        }

        missing.Should().BeEmpty("every DocTooltip.Key literal in XAML must be registered in UiDocs");
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SynthEBD.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
