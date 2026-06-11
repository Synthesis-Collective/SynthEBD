using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

// R12: the pure static formatters were extracted from Logger into LogFormatting so they can be
// exercised with plain inputs (no Mutagen/VM object graph). These characterize the format helpers
// that previously had no direct coverage; Logger keeps thin forwarders, so these also protect the
// historic Logger.X(...) call sites.
public class LogFormattingTests
{
    [Fact]
    public void DateTimeToHMS_ZeroPadsHoursMinutesSeconds()
    {
        var dt = new DateTime(2024, 1, 2, 4, 3, 9);
        LogFormatting.DateTimeToHMS(dt).Should().Be("04:03:09");
    }

    [Fact]
    public void FormatTimeStamp_WrapsHMSInBracketsWithTrailingSpace()
    {
        var dt = new DateTime(2024, 1, 2, 14, 3, 9);
        LogFormatting.FormatTimeStamp(dt).Should().Be("[14:03:09] ");
    }

    [Fact]
    public void FormatLogStringIndents_IndentsByTagDepth()
    {
        var expected = string.Join(Environment.NewLine, "<a>", "\t<b>x</b>", "\t</a>");
        LogFormatting.FormatLogStringIndents("<a><b>x</b></a>").Should().Be(expected);
    }

    [Fact]
    public void GetBodyShapeDescriptorString_JoinsCategoriesWithPipe()
    {
        var descriptors = new Dictionary<string, HashSet<string>>
        {
            { "Build", new HashSet<string> { "Curvy", "Petite" } },
            { "Bust", new HashSet<string> { "Large" } },
        };

        LogFormatting.GetBodyShapeDescriptorString(descriptors)
            .Should().Be("Build: [Curvy, Petite] | Bust: [Large]");
    }

    [Fact]
    public void GetBodyShapeDescriptorString_EmptyMap_ReturnsEmpty()
    {
        LogFormatting.GetBodyShapeDescriptorString(new Dictionary<string, HashSet<string>>())
            .Should().BeEmpty();
    }
}
