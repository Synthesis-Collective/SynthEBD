using System;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B42: the crash handler built its log path from _settingsSourceProvider.GetCurrentSettingsRootPath()
/// with no null guard, so a crash before that field resolved made the crash handler itself NRE and lose
/// the report. BuildCrashLogPath now falls back to a provided root (the app base directory) when the
/// settings root is null/blank. Windows separators (the suite/CI run net8.0-windows).
/// </summary>
public class AppCrashLogPathTests
{
    private static readonly DateTime Ts = new DateTime(2026, 6, 8, 13, 55, 0);

    [Fact]
    public void BuildCrashLogPath_UsesSettingsRoot_WhenPresent()
    {
        App.BuildCrashLogPath(@"C:\Settings", @"C:\Fallback", Ts)
            .Should().Be(@"C:\Settings\Logs\Crash Logs\2026-06-08-13-55.txt");
    }

    [Fact]
    public void BuildCrashLogPath_FallsBackToBaseDirectory_WhenSettingsRootNull()
    {
        App.BuildCrashLogPath(null, @"C:\Fallback", Ts)
            .Should().Be(@"C:\Fallback\Logs\Crash Logs\2026-06-08-13-55.txt");
    }

    [Fact]
    public void BuildCrashLogPath_FallsBackToBaseDirectory_WhenSettingsRootBlank()
    {
        App.BuildCrashLogPath("   ", @"C:\Fallback", Ts)
            .Should().Be(@"C:\Fallback\Logs\Crash Logs\2026-06-08-13-55.txt");
    }
}
