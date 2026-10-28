using System.IO;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class OutputFolderGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SynthEBD_OutputGuard_" + Guid.NewGuid().ToString("N"));

    public OutputFolderGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Dir(params string[] parts) => Path.Combine(new[] { _root }.Concat(parts).ToArray());

    [Fact]
    public void ModFolderContainingPortableSettings_IsConflict()
    {
        // The 2026-10-27 data loss: output = mod folder, settings = <mod folder>\SynthEBD.
        var conflict = OutputFolderGuard.GetSettingsConflict(Dir("Mod"), Dir("Mod", "SynthEBD"), Dir("Data"));
        conflict.Should().NotBeNull();
    }

    [Fact]
    public void SameFolder_IsConflict()
    {
        OutputFolderGuard.GetSettingsConflict(Dir("Mod", "SynthEBD"), Dir("Mod", "SynthEBD") + Path.DirectorySeparatorChar, Dir("Data"))
            .Should().NotBeNull();
    }

    [Fact]
    public void OutputInsideSettings_IsConflict()
    {
        OutputFolderGuard.GetSettingsConflict(Dir("Mod", "SynthEBD", "Out"), Dir("Mod", "SynthEBD"), Dir("Data"))
            .Should().NotBeNull();
    }

    [Fact]
    public void SeparateFolders_AreAllowed()
    {
        OutputFolderGuard.GetSettingsConflict(Dir("Output Mod"), Dir("Settings Mod", "SynthEBD"), Dir("Data"))
            .Should().BeNull();
    }

    [Fact]
    public void SiblingWithSharedPrefix_IsAllowed()
    {
        OutputFolderGuard.GetSettingsConflict(Dir("Mod"), Dir("Mod2", "SynthEBD"), Dir("Data"))
            .Should().BeNull();
    }

    [Fact]
    public void DataFolderOutput_IsNeverCleared_SoAllowed()
    {
        OutputFolderGuard.GetSettingsConflict(Dir("Data"), Dir("Data", "SynthEBD"), Dir("Data"))
            .Should().BeNull();
    }

    [Fact]
    public void SettingsReachedThroughAnotherPath_DetectedByContent()
    {
        // A virtual-file-system alias: the paths differ, but the cleared subfolder physically holds settings.
        Directory.CreateDirectory(Dir("Mod", "SynthEBD", "Settings"));
        File.WriteAllText(Dir("Mod", "SynthEBD", "Settings", "GeneralSettings.json"), "{}");

        OutputFolderGuard.GetSettingsConflict(Dir("Mod"), Dir("Data", "SynthEBD"), Dir("Data"))
            .Should().NotBeNull();
    }

    [Fact]
    public void ClearedSubfolderHoldingSettings_IsDetectedDirectly()
    {
        // ClearPreviousScriptOutputs checks each cleared subfolder itself before deleting it.
        Directory.CreateDirectory(Dir("Mod", "SynthEBD", "Settings"));
        File.WriteAllText(Dir("Mod", "SynthEBD", "Settings", "GeneralSettings.json"), "{}");

        OutputFolderGuard.LooksLikeSettingsFolder(Dir("Mod", "SynthEBD")).Should().BeTrue();
        OutputFolderGuard.LooksLikeSettingsFolder(Dir("Mod", "Scripts")).Should().BeFalse();
    }
}
