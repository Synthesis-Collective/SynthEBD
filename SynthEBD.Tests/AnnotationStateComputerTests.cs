using FluentAssertions;
using Xunit;

namespace SynthEBD.Tests;

public class AnnotationStateComputerTests
{
    private sealed class FakeAnnotated : IHasAnnotationState
    {
        public BodyShapeAnnotationState AnnotationState { get; set; }
    }

    private static List<IHasAnnotationState> Sub(params BodyShapeAnnotationState[] states)
    {
        var list = new List<IHasAnnotationState>();
        foreach (var s in states) { list.Add(new FakeAnnotated { AnnotationState = s }); }
        return list;
    }

    // R9: ComputeAnnotationState rolls child substates into one state -- Manual / RulesBased if only that kind
    // is present, Mix if both, otherwise None. These cases lock that mapping after the inert
    // "if (!IsAnnotated(subStates)) state = None" no-op block (and its orphaned helper) were removed; the
    // Manual+None / RulesBased+None cases specifically prove the removed "any substate is None" check did not
    // affect the result.
    [Fact]
    public void EmptyList_ReturnsNone() =>
        AnnotationStateComputer.ComputeAnnotationState(Sub())
            .Should().Be(BodyShapeAnnotationState.None);

    [Fact]
    public void AllNone_ReturnsNone() =>
        AnnotationStateComputer.ComputeAnnotationState(Sub(BodyShapeAnnotationState.None, BodyShapeAnnotationState.None))
            .Should().Be(BodyShapeAnnotationState.None);

    [Fact]
    public void OnlyManual_ReturnsManual() =>
        AnnotationStateComputer.ComputeAnnotationState(Sub(BodyShapeAnnotationState.Manual, BodyShapeAnnotationState.None))
            .Should().Be(BodyShapeAnnotationState.Manual);

    [Fact]
    public void OnlyRulesBased_ReturnsRulesBased() =>
        AnnotationStateComputer.ComputeAnnotationState(Sub(BodyShapeAnnotationState.RulesBased, BodyShapeAnnotationState.None))
            .Should().Be(BodyShapeAnnotationState.RulesBased);

    [Fact]
    public void BothManualAndRulesBased_ReturnsMix() =>
        AnnotationStateComputer.ComputeAnnotationState(Sub(BodyShapeAnnotationState.Manual, BodyShapeAnnotationState.RulesBased))
            .Should().Be(BodyShapeAnnotationState.Mix_Manual_RulesBased);
}
