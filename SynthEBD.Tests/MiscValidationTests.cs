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
}
