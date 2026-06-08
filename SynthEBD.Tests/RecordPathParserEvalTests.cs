using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace SynthEBD.Tests;

public class RecordPathParserEvalTests
{
    // B14: PatchableRaces was held as two hand-synced collections (a HashSet<IFormLinkGetter<IRaceGetter>>
    // and a HashSet<FormKey>); ResolvePatchableRaces rebuilt one fresh but only ever *added* to the other,
    // so repeated calls let stale FormKeys accumulate and the two drifted. They were collapsed to a single
    // HashSet<FormKey>. The form-link set was also fed into RecordPathParser's DynamicExpresso evaluator for
    // PatchableRaces.Contains(...) conditions; collapsing means the evaluator now sees HashSet<FormKey>.Contains(FormKey)
    // instead of HashSet<IFormLinkGetter<IRaceGetter>>.Contains(IFormLinkGetter<IRaceGetter>). These tests pin
    // that the evaluator still resolves the Contains overload and that FormKey equality holds through it, which
    // is the exact expression shape the PatchableRaces site now builds: params [FormKey, HashSet<FormKey>], "_1.Contains(_0)".
    private static readonly FormKey NordRace = FormKey.Factory("013746:Skyrim.esm");
    private static readonly FormKey ImperialRace = FormKey.Factory("013745:Skyrim.esm");
    private static readonly FormKey BretonRace = FormKey.Factory("013741:Skyrim.esm");

    [Fact]
    public void PatchableRacesExpression_MemberPresent_ReturnsTrue()
    {
        var patchableRaces = new HashSet<FormKey> { NordRace, ImperialRace };
        var parameters = new List<dynamic> { NordRace, patchableRaces };

        RecordPathParser.EvalBoolExpression("_1.Contains(_0)", parameters).Should().BeTrue();
    }

    [Fact]
    public void PatchableRacesExpression_MemberAbsent_ReturnsFalse()
    {
        var patchableRaces = new HashSet<FormKey> { NordRace, ImperialRace };
        var parameters = new List<dynamic> { BretonRace, patchableRaces };

        RecordPathParser.EvalBoolExpression("_1.Contains(_0)", parameters).Should().BeFalse();
    }

    [Fact]
    public void PatchableRacesExpression_NegatedMember_ReturnsFalse()
    {
        // The condition can also be written as a negation; verify the FormKey set composes inside a larger expression.
        var patchableRaces = new HashSet<FormKey> { NordRace };
        var parameters = new List<dynamic> { NordRace, patchableRaces };

        RecordPathParser.EvalBoolExpression("!_1.Contains(_0)", parameters).Should().BeFalse();
    }
}
