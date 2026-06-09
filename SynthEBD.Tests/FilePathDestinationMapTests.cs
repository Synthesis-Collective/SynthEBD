using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

/// <summary>
/// B39: FileNameToDestMap routed Source_HeadSpecularArgonianFemale ("argonianfemalehead_s.dds", a head
/// specular) to Dest_TorsoFemaleSpecular (the body/worn-armor skin specular slot) instead of
/// Dest_HeadSpecular -- a copy-paste slip, since the Argonian-female torso specular already has its own
/// correct entry and every other head specular maps to the head. This reflection invariant asserts that
/// every Source_HeadSpecular* constant present in the map routes to the head specular destination.
/// </summary>
public class FilePathDestinationMapTests
{
    [Fact]
    public void EveryHeadSpecularSource_MapsToHeadSpecularDestination()
    {
        var headSpecularSources = typeof(FilePathDestinationMap)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.StartsWith("Source_HeadSpecular"))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        headSpecularSources.Should().NotBeEmpty(); // guard against the reflection filter silently matching nothing

        foreach (var source in headSpecularSources)
        {
            if (FilePathDestinationMap.FileNameToDestMap.TryGetValue(source, out var dest))
            {
                dest.Should().Be(FilePathDestinationMap.Dest_HeadSpecular,
                    "head specular source '{0}' must route to the head specular slot, not the body", source);
            }
        }
    }
}
