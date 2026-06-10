using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class VM_LinkedNPCGroupTests
{
    // B51: DumpViewModelsToModels recovered the primary FormKey via Primary.Split('|')[2], which mis-indexed when
    // an NPC name contained '|' (taking a middle field) and threw IndexOutOfRange on a string with fewer than three
    // '|' fields. GetTrailingPipeField takes the last '|' field (always the FormKey, since EditorID/FormKey cannot
    // contain '|') and trims it, so the caller's FormKey.TryFactory degrades gracefully instead of mis-parsing/throwing.
    [Theory]
    [InlineData("Lydia | Lydia | 000A2C94:Skyrim.esm", "000A2C94:Skyrim.esm")]   // normal 3-field
    [InlineData("Guard | Whiterun | GuardEDID | 0F1234:Mod.esp", "0F1234:Mod.esp")] // '|' in name -> >3 fields (old [2] = "GuardEDID")
    [InlineData("000A2C94:Skyrim.esm", "000A2C94:Skyrim.esm")]                    // no pipe (old [2] threw)
    [InlineData("Name | EDID |  0F1234:Mod.esp ", "0F1234:Mod.esp")]              // leading/trailing whitespace trimmed
    public void GetTrailingPipeField_ReturnsTrimmedLastField(string input, string expected)
    {
        VM_LinkedNPCGroup.GetTrailingPipeField(input).Should().Be(expected);
    }
}
