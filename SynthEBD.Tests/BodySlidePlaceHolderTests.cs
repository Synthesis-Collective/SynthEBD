using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class BodySlidePlaceHolderTests
{
    // B27: RenameByIndex used Label.TrimEnd(index.ToString().ToArray()), trimming a CHARACTER SET. Because the
    // index came from int.Parse it had its leading zeros dropped, so a zero-padded suffix like "Body007" trimmed
    // only the '7' and left "Body00". ReplaceTrailingNumber strips the exact trailing digit run by length instead.
    [Theory]
    [InlineData("Body007", 8, "Body8")]            // the fix: zero-padded suffix fully replaced (was "Body008")
    [InlineData("CBBE Outfit 007", 2, "CBBE Outfit 2")]
    [InlineData("Body22", 23, "Body23")]           // normal multi-digit suffix still works
    [InlineData("Body", 2, "Body2")]               // no trailing number -> append
    [InlineData("Body2", 3, "Body3")]              // single-digit suffix replaced
    public void ReplaceTrailingNumber_ReplacesExactDigitRun(string label, int newIndex, string expected)
    {
        VM_BodySlidePlaceHolder.ReplaceTrailingNumber(label, newIndex).Should().Be(expected);
    }
}
