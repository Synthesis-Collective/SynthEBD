using System;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B36: the OBody trainer saved its ML model to a path built with a bare DateTime.Now.ToString() (no
/// format/culture), which on most locales yields '/' and ':' -- invalid Windows path characters -- so
/// context.Model.Save threw. BuildModelFileName now uses the sortable invariant timestamp the sibling
/// CSV export already used, producing a path-safe name.
/// </summary>
public class OBodyTrainerExporterTests
{
    [Fact]
    public void BuildModelFileName_UsesSortableInvariantTimestamp()
    {
        var name = VM_OBodyTrainerExporter.BuildModelFileName("Hips", new DateTime(2026, 6, 8, 13, 55, 0));
        name.Should().Be("Hips_2026-06-08-13-55");
    }

    [Fact]
    public void BuildModelFileName_ContainsNoInvalidPathCharacters()
    {
        // The old bare DateTime.Now.ToString() injected '/' and ':' (e.g. "6/8/2026 1:55:39 PM").
        var name = VM_OBodyTrainerExporter.BuildModelFileName("Hips", new DateTime(2026, 6, 8, 13, 55, 0));
        name.Should().NotContainAny("/", "\\", ":");
    }
}
