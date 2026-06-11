using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class MiscValidationTests
{
    // B16: the RaceMenu-ini "Could not parse X" messages gated the "( in line: ...)" locator on the wrong
    // out-variable (morphLine) while appending another (genLine/scaleLine), so the hint's presence was coupled
    // to an unrelated setting and could be suppressed-when-known or appended-blank. The fix routes every site
    // through AppendIniLineReference(message, line), which takes the gated value as a single parameter so the
    // guard and the appended text cannot diverge.
    [Fact]
    public void AppendIniLineReference_KnownLine_AppendsLocator()
    {
        MiscValidation.AppendIniLineReference("Could not parse bEnableBodyGen in skee64.ini", "bEnableBodyGen=")
            .Should().Be("Could not parse bEnableBodyGen in skee64.ini( in line: bEnableBodyGen=)");
    }

    [Fact]
    public void AppendIniLineReference_EmptyLine_ReturnsMessageUnchanged()
    {
        MiscValidation.AppendIniLineReference("Could not parse bEnableBodyGen in skee64.ini", "")
            .Should().Be("Could not parse bEnableBodyGen in skee64.ini");
    }

    // R1: CheckDataFiles is the shared pure seam behind the simple Verify*Installed helpers. It composes each
    // descriptor's full path under the Data folder and, for every file the injected probe reports missing,
    // appends "Could not find {file} from {source} at {path}" -- then a single install hint if anything was
    // missing. The file probe is injected so the message/path assembly is unit-testable without a Data folder.
    [Fact]
    public void CheckDataFiles_AllPresent_ReturnsTrueWithNoMessages()
    {
        var verified = MiscValidation.CheckDataFiles(
            @"C:\Data",
            new[] { (@"Scripts\A.pex", "Mod A"), (@"SKSE\Plugins\B.dll", "Mod B") },
            "Please make sure that Mod is installed.",
            _ => true,
            out var messages);

        verified.Should().BeTrue();
        messages.Should().BeEmpty();
    }

    [Fact]
    public void CheckDataFiles_FileMissing_AppendsNotFoundMessageAndHint()
    {
        var verified = MiscValidation.CheckDataFiles(
            @"C:\Data",
            new[] { (@"Scripts\A.pex", "Mod A"), (@"SKSE\Plugins\B.dll", "Mod B") },
            "Please make sure that Mod is installed.",
            path => path.EndsWith("A.pex"), // only A.pex exists
            out var messages);

        verified.Should().BeFalse();
        messages.Should().Equal(
            @"Could not find B.dll from Mod B at C:\Data\SKSE\Plugins\B.dll",
            "Please make sure that Mod is installed.");
    }

    [Fact]
    public void CheckDataFiles_MultipleMissing_PreservesOrderThenHint()
    {
        var verified = MiscValidation.CheckDataFiles(
            @"C:\Data",
            new[] { (@"Scripts\A.pex", "Mod A"), (@"SKSE\Plugins\B.dll", "Mod B") },
            "Please make sure that Mod is installed.",
            _ => false, // nothing exists
            out var messages);

        verified.Should().BeFalse();
        messages.Should().Equal(
            @"Could not find A.pex from Mod A at C:\Data\Scripts\A.pex",
            @"Could not find B.dll from Mod B at C:\Data\SKSE\Plugins\B.dll",
            "Please make sure that Mod is installed.");
    }

    [Fact]
    public void CheckDataFiles_NullHint_OmitsHintWhenMissing()
    {
        var verified = MiscValidation.CheckDataFiles(
            @"C:\Data",
            new[] { (@"SKSE\Plugins\B.dll", "Mod B") },
            null,
            _ => false,
            out var messages);

        verified.Should().BeFalse();
        messages.Should().ContainSingle()
            .Which.Should().Be(@"Could not find B.dll from Mod B at C:\Data\SKSE\Plugins\B.dll");
    }
}
