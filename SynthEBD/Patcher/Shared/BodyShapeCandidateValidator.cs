namespace SynthEBD;

/// <summary>
/// Shared rule battery for body-shape candidates (R18), replacing the ~95%-duplicated validators in
/// BodyGenSelector (MorphIsValid) and OBodySelector (PresetIsValid). Checks run in the original order:
/// specific-assignment exemption, unique/non-unique flags, allowed/disallowed races (optionally ignored),
/// weight range, allowed/disallowed attributes (tallying ForceIf matches in <see cref="NPCInfo.ForceIfMatches"/>),
/// the candidate's own descriptor rules, the allowed/disallowed descriptors imposed by every assigned asset
/// combination (axis-parameterized via <see cref="GetAssetDescriptorRules"/> so the BodyGen and BodySlide
/// rule properties cannot be cross-wired — the B62 bug class), and finally the AllowRandom gate (last, so
/// ForceIf matches can override it — B59 ordering).
/// The NPC-static portion (everything except the asset-imposed checks and the gate) is cached per NPC (R15);
/// the cache is bypassed for verbose-logged NPCs so their reports show the full reasoning for every attempt.
/// </summary>
public class BodyShapeCandidateValidator
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly AttributeMatcher _attributeMatcher;

    /// <summary>Injects the environment (race-name resolution for logs), patcher state (verbose-attribute setting), logging, and attribute matching.</summary>
    public BodyShapeCandidateValidator(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, AttributeMatcher attributeMatcher)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _attributeMatcher = attributeMatcher;
    }

    /// <summary>Which body-shape axis a validation runs under; selects the asset packs'/subgroups' BodyGen vs BodySlide descriptor rule properties.</summary>
    public enum BodyShapeAxis
    {
        BodyGen,
        BodySlide,
    }

    /// <summary>A candidate's NPC-static verdict (independent of the currently-assigned asset combinations), cacheable per NPC.</summary>
    public enum StaticValidity
    {
        /// <summary>Fails an NPC-static rule; never valid for this NPC.</summary>
        Invalid,
        /// <summary>Specifically assigned by the user: valid, skipping the asset-imposed checks and the AllowRandom gate.</summary>
        ExemptViaSpecificAssignment,
        /// <summary>Passes the NPC-static rules; the per-combination asset-imposed checks and the AllowRandom gate still apply.</summary>
        ValidPendingDynamicChecks,
    }

    /// <summary>Axis- and call-specific validation inputs; build once per selector call, not per candidate.</summary>
    public class ValidationContext
    {
        /// <summary>"Morph" or "Preset" — keeps the report wording per-axis.</summary>
        public string Noun { get; set; }
        public BodyShapeAxis Axis { get; set; }
        /// <summary>Attribute groups resolved for this axis (the BodyGen config's local set, or the OBody settings' set).</summary>
        public HashSet<AttributeGroup> AttributeGroups { get; set; }
        /// <summary>The axis's descriptor catalog (TemplateDescriptors), flattened once per call.</summary>
        public List<BodyShapeDescriptor> DescriptorCatalog { get; set; }
        /// <summary>True to skip the allowed/disallowed race checks (BodyGen's relaxed Specific-assignment retry).</summary>
        public bool IgnoreRaceChecks { get; set; }
        /// <summary>Tests whether the candidate is specifically assigned by the user (exempting it from all other rules), or null when no exemption applies.</summary>
        public Func<IBodyShapeRuleCandidate, bool> IsSpecificallyAssigned { get; set; }
    }

    /// <summary>
    /// Full validity check for one candidate: the (cached) NPC-static rules, then the asset-imposed
    /// descriptor checks for the currently-assigned combinations, then the AllowRandom gate.
    /// </summary>
    /// <returns>True if the candidate may be assigned to the NPC alongside <paramref name="assignedAssetCombinations"/>.</returns>
    public bool CandidateIsValid(IBodyShapeRuleCandidate candidate, NPCInfo npcInfo, ValidationContext context, IEnumerable<SubgroupCombination> assignedAssetCombinations)
    {
        var candidateDescriptors = candidate.GetDescriptorsForValidation(npcInfo.NPC.Weight);

        var staticCache = context.IgnoreRaceChecks ? npcInfo.BodyShapeStaticValidityRaceIgnored : npcInfo.BodyShapeStaticValidity;
        // verbose-logged NPCs always recompute so their reports show the full reasoning for every combination attempt
        if (npcInfo.Report.LogCurrentNPC || !staticCache.TryGetValue(candidate, out var staticValidity))
        {
            staticValidity = EvaluateNpcStaticRules(candidate, candidateDescriptors, npcInfo, context);
            staticCache[candidate] = staticValidity;
        }

        if (staticValidity == StaticValidity.Invalid)
        {
            return false;
        }
        if (staticValidity == StaticValidity.ExemptViaSpecificAssignment)
        {
            return true;
        }

        if (!CompatibleWithAssetCombinations(candidate.Label, candidateDescriptors, npcInfo, context, assignedAssetCombinations))
        {
            return false;
        }

        // checked last so a ForceIf match (including descriptor-derived ones) can override the random-distribution opt-out (B59)
        if (!candidate.AllowRandom && npcInfo.ForceIfMatches.Get(candidate) == 0)
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because it can only be assigned via ForceIf attributes or Specific NPC Assignments", false, npcInfo);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The NPC-static rule sequence: specific-assignment exemption, unique/non-unique, races, weight range,
    /// allowed/disallowed attributes (tallying ForceIf matches), and the candidate's own descriptor rules
    /// (accumulating descriptor-derived ForceIf matches). Depends only on the NPC and candidate, never on the
    /// assigned asset combinations.
    /// </summary>
    private StaticValidity EvaluateNpcStaticRules(IBodyShapeRuleCandidate candidate, HashSet<BodyShapeDescriptor.LabelSignature> candidateDescriptors, NPCInfo npcInfo, ValidationContext context)
    {
        if (context.IsSpecificallyAssigned != null && context.IsSpecificallyAssigned(candidate))
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is valid because it is specifically assigned by user.", false, npcInfo);
            return StaticValidity.ExemptViaSpecificAssignment;
        }

        // Allow unique NPCs
        if (!candidate.AllowUnique && npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because it is disallowed for unique NPCs", false, npcInfo);
            return StaticValidity.Invalid;
        }

        // Allow non-unique NPCs
        if (!candidate.AllowNonUnique && !npcInfo.NPC.Configuration.Flags.HasFlag(Mutagen.Bethesda.Skyrim.NpcConfiguration.Flag.Unique))
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because it is disallowed for non-unique NPCs", false, npcInfo);
            return StaticValidity.Invalid;
        }

        if (!context.IgnoreRaceChecks)
        {
            // Allowed Races
            if (candidate.AllowedRaces.Any() && !candidate.AllowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because its allowed races (" + Logger.GetRaceListLogStrings(candidate.AllowedRaces, _environmentProvider.LinkCache, _patcherState) + ") do not include the current NPC's race", false, npcInfo);
                return StaticValidity.Invalid;
            }

            // Disallowed Races
            if (candidate.DisallowedRaces.Contains(npcInfo.BodyShapeRace))
            {
                _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because its disallowed races (" + Logger.GetRaceListLogStrings(candidate.DisallowedRaces, _environmentProvider.LinkCache, _patcherState) + ") include the current NPC's race", false, npcInfo);
                return StaticValidity.Invalid;
            }
        }

        // Weight Range
        if (npcInfo.NPC.Weight < candidate.WeightRange.Lower || npcInfo.NPC.Weight > candidate.WeightRange.Upper)
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because the current NPC's weight falls outside of its allowed weight range", false, npcInfo);
            return StaticValidity.Invalid;
        }

        // Allowed and Forced Attributes
        npcInfo.ForceIfMatches.Set(candidate, 0);
        _attributeMatcher.MatchNPCtoAttributeList(candidate.AllowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, context.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out bool hasAttributeRestrictions, out bool matchesAttributeRestrictions, out int matchedForceIfWeightedCount, out string _, out string unmatchedLog, out string forceIfLog, null);
        if (hasAttributeRestrictions && !matchesAttributeRestrictions)
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because the NPC does not match any of its allowed attributes: " + unmatchedLog, false, npcInfo);
            return StaticValidity.Invalid;
        }
        else
        {
            npcInfo.ForceIfMatches.Set(candidate, matchedForceIfWeightedCount);
        }

        if (npcInfo.ForceIfMatches.Get(candidate) > 0)
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " Current NPC matches the following forced attributes: " + forceIfLog, false, npcInfo);
        }

        // Disallowed Attributes
        _attributeMatcher.MatchNPCtoAttributeList(candidate.DisallowedAttributes, npcInfo.NPC, npcInfo.BodyShapeRace, context.AttributeGroups, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out hasAttributeRestrictions, out matchesAttributeRestrictions, out int dummy, out string matchLog, out string _, out string _, null);
        if (hasAttributeRestrictions && matchesAttributeRestrictions)
        {
            _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because the NPC matches one of its disallowed attributes: " + matchLog, false, npcInfo);
            return StaticValidity.Invalid;
        }

        // Repeat the above checks for the candidate's own descriptor rules
        foreach (var descriptorLabel in candidateDescriptors)
        {
            var associatedDescriptor = context.DescriptorCatalog.FirstOrDefault(x => x.ID.MapsTo(descriptorLabel));
            if (associatedDescriptor is not null)
            {
                if (associatedDescriptor.PermitNPC(npcInfo, context.AttributeGroups, _attributeMatcher, _patcherState.GeneralSettings.VerboseModeDetailedAttributes, out string reportStr, out int descriptorForceIfCount))
                {
                    if (descriptorForceIfCount > 0)
                    {
                        npcInfo.ForceIfMatches.Add(candidate, descriptorForceIfCount);
                        _logger.LogReport(reportStr, false, npcInfo);
                    }
                }
                else
                {
                    _logger.LogReport(() => context.Noun + " " + candidate.Label + " is invalid because the rules for its descriptor " + reportStr, false, npcInfo);
                    return StaticValidity.Invalid;
                }
            }
        }

        return StaticValidity.ValidPendingDynamicChecks;
    }

    /// <summary>
    /// The per-combination checks: the candidate's descriptors must satisfy the whole-config and per-subgroup
    /// allowed/disallowed descriptor rules of every assigned asset combination, using the axis-appropriate
    /// rule properties.
    /// </summary>
    private bool CompatibleWithAssetCombinations(string candidateLabel, HashSet<BodyShapeDescriptor.LabelSignature> candidateDescriptors, NPCInfo npcInfo, ValidationContext context, IEnumerable<SubgroupCombination> assignedAssetCombinations)
    {
        if (assignedAssetCombinations == null)
        {
            return true;
        }

        foreach (var assignedAssetCombination in assignedAssetCombinations)
        {
            // check whole config rules
            var configRules = GetAssetDescriptorRules(assignedAssetCombination.AssetPack.DistributionRules, context.Axis);
            if (configRules.Allowed.Any())
            {
                if (!BodyShapeDescriptor.DescriptorsMatch(configRules.Allowed, candidateDescriptors, configRules.AllowedMode, out _))
                {
                    _logger.LogReport(() => context.Noun + " " + candidateLabel + " is invalid because its descriptors do not match allowed descriptors from assigned Asset Pack " + assignedAssetCombination.AssignmentName + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(configRules.Allowed), false, npcInfo);
                    return false;
                }
            }

            if (BodyShapeDescriptor.DescriptorsMatch(configRules.Disallowed, candidateDescriptors, configRules.DisallowedMode, out string matchedDescriptor))
            {
                _logger.LogReport(() => context.Noun + " " + candidateLabel + " is invalid because its descriptor [" + matchedDescriptor + "] is disallowed by assigned Asset Pack " + assignedAssetCombination.AssignmentName, false, npcInfo);
                return false;
            }

            // check subgroups
            foreach (var subgroup in assignedAssetCombination.ContainedSubgroups)
            {
                var subgroupRules = GetAssetDescriptorRules(subgroup, context.Axis);
                if (subgroupRules.Allowed.Any())
                {
                    if (!BodyShapeDescriptor.DescriptorsMatch(subgroupRules.Allowed, candidateDescriptors, subgroupRules.AllowedMode, out _))
                    {
                        _logger.LogReport(() => context.Noun + " " + candidateLabel + " is invalid because its descriptors do not match allowed descriptors from assigned subgroup " + Logger.GetSubgroupIDString(subgroup) + Environment.NewLine + "\t" + Logger.GetBodyShapeDescriptorString(subgroupRules.Allowed), false, npcInfo);
                        return false;
                    }
                }

                if (BodyShapeDescriptor.DescriptorsMatch(subgroupRules.Disallowed, candidateDescriptors, subgroupRules.DisallowedMode, out matchedDescriptor))
                {
                    var matched = matchedDescriptor;
                    _logger.LogReport(() => context.Noun + " " + candidateLabel + " is invalid because its descriptor [" + matched + "] is disallowed by assigned subgroup " + Logger.GetSubgroupIDString(subgroup), false, npcInfo);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Selects the axis-appropriate allowed/disallowed descriptor rule properties from an asset pack's DistributionRules or a subgroup.</summary>
    private static (Dictionary<string, HashSet<string>> Allowed, DescriptorMatchMode AllowedMode, Dictionary<string, HashSet<string>> Disallowed, DescriptorMatchMode DisallowedMode) GetAssetDescriptorRules(FlattenedSubgroup assetRules, BodyShapeAxis axis)
    {
        return axis switch
        {
            BodyShapeAxis.BodyGen => (assetRules.AllowedBodyGenDescriptors, assetRules.AllowedBodyGenMatchMode, assetRules.DisallowedBodyGenDescriptors, assetRules.DisallowedBodyGenMatchMode),
            _ => (assetRules.AllowedBodySlideDescriptors, assetRules.AllowedBodySlideMatchMode, assetRules.DisallowedBodySlideDescriptors, assetRules.DisallowedBodySlideMatchMode),
        };
    }
}
