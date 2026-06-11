using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Annotations.Storage;

namespace SynthEBD;

/// <summary>Aggregates the annotation states of a set of body-shape descriptor sub-items into a single
/// rolled-up <see cref="BodyShapeAnnotationState"/> (None / Manual / RulesBased / a mix) for display.</summary>
public class AnnotationStateComputer
{
    /// <summary>Rolls up the child <paramref name="subStates"/> into one state: <c>Manual</c> or
    /// <c>RulesBased</c> if only that kind is present, <c>Mix_Manual_RulesBased</c> if both, otherwise
    /// <c>None</c>.</summary>
    public static BodyShapeAnnotationState ComputeAnnotationState(ICollection<IHasAnnotationState> subStates)
    {
        // Default to None; the manual/rules-based chain below overrides it when either kind is present
        // (when no substate is Manual or RulesBased, the state stays None).
        BodyShapeAnnotationState state = BodyShapeAnnotationState.None;
        bool hasManual = HasManualDescriptors(subStates);
        bool hasRulesBased = HasRulesBasedDescriptors(subStates);

        if (hasManual && !hasRulesBased)
        {
            state = BodyShapeAnnotationState.Manual;
        }
        else if (!hasManual && hasRulesBased)
        {
            state = BodyShapeAnnotationState.RulesBased;
        }
        else if (hasManual && hasRulesBased)
        {
            state = BodyShapeAnnotationState.Mix_Manual_RulesBased;
        }
        return state;
    }

    /// <summary>Returns whether any substate has a <c>Manual</c> annotation state.</summary>
    private static bool HasManualDescriptors(ICollection<IHasAnnotationState> subStates)
    {
        return subStates.Any(x => x.AnnotationState == BodyShapeAnnotationState.Manual);
    }
    /// <summary>Returns whether any substate has a <c>RulesBased</c> annotation state.</summary>
    private static bool HasRulesBasedDescriptors(ICollection<IHasAnnotationState> subStates)
    {
        return subStates.Any(x => x.AnnotationState == BodyShapeAnnotationState.RulesBased);
    }
}

/// <summary>Implemented by items that carry a <see cref="BodyShapeAnnotationState"/> and can be rolled
/// up by <see cref="AnnotationStateComputer"/>.</summary>
public interface IHasAnnotationState
{
    /// <summary>This item's annotation state.</summary>
    public BodyShapeAnnotationState AnnotationState { get; set; }
}
