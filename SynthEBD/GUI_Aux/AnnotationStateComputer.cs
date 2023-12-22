using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Annotations.Storage;

namespace SynthEBD;

public class AnnotationStateComputer
{
    public static BodyShapeAnnotationState ComputeAnnotationState(ICollection<IHasAnnotationState> subStates)
    {
        BodyShapeAnnotationState state = new();
        if (!IsAnnotated(subStates))
        {
            state = BodyShapeAnnotationState.None;
        }
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

    private static bool IsAnnotated(ICollection<IHasAnnotationState> subStates)
    {
        return subStates.Where(x => x.AnnotationState == BodyShapeAnnotationState.None).Any();
    }
    private static bool HasManualDescriptors(ICollection<IHasAnnotationState> subStates)
    {
        return subStates.Where(x => x.AnnotationState == BodyShapeAnnotationState.Manual).Any();
    }
    private static bool HasRulesBasedDescriptors(ICollection<IHasAnnotationState> subStates)
    {
        return subStates.Where(x => x.AnnotationState == BodyShapeAnnotationState.RulesBased).Any();
    }
}

public interface IHasAnnotationState
{
    public BodyShapeAnnotationState AnnotationState { get; set; }
}
