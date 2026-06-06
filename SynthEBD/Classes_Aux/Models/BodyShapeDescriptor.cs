using Newtonsoft.Json;
using Synthesis.Bethesda.Execution.DotNet;
using System.Diagnostics;

namespace SynthEBD;

/// <summary>
/// One body-shape descriptor value: a Category+Value identity (<see cref="ID"/>), the allow/disallow
/// rules that gate it (<see cref="AssociatedRules"/>), and helpers for matching it against NPCs and
/// descriptor sets. Descriptors annotate BodySlide presets / BodyGen morphs so the assigner can pick
/// shapes that fit an NPC.
/// </summary>
[DebuggerDisplay("{ID.ToString()}")]
public class BodyShapeDescriptor : IHasLabel
{
    /// <summary>Creates an empty descriptor (used everywhere except backward-compatible deserialization).</summary>
    public BodyShapeDescriptor()
    {
        // empty - used for everything besides backward-compatibile deserialization
    }

    /// <summary>Deserialization constructor accepting either the deprecated <paramref name="deprecated"/> signature or a separate category/value pair, normalizing both into <see cref="ID"/>.</summary>
    /// <param name="deprecated">Legacy combined signature (used if non-null).</param>
    /// <param name="category">Category, used when <paramref name="deprecated"/> is null.</param>
    /// <param name="Value">Value, used when <paramref name="deprecated"/> is null.</param>
    [JsonConstructor] // https://stackoverflow.com/questions/73675879/newtonsoft-json-if-deserialization-error-try-different-class
    public BodyShapeDescriptor(LabelSignature deprecated, string category, string Value)
    {
        if (deprecated != null)
        {
            ID.Category = deprecated.Category;
            ID.Value = deprecated.Value;
        }
        else
        {
            ID = new() { Category = category, Value = Value };
        }
    }

    public LabelSignature ID { get; set; } = new();
    public BodyShapeDescriptorRules AssociatedRules { get; set; } = new();
    public string Label { get; set; } // for duplicate removal - populated by removal code
    // CategoryDescription was historically stored on every value entry, even though it logically
    // belongs to the Category. As of 2026 it lives on BodyShapeDescriptorShell.CategoryDescription
    // alongside the Category itself; the legacy field is handled at load time by
    // BodyShapeDescriptorShellListConverter so pre-refactor JSON files still load cleanly.
    public string ValueDescription { get; set; }

    /// <summary>Determines whether this descriptor has the same Category+Value as another descriptor or label signature.</summary>
    /// <param name="obj">A <see cref="BodyShapeDescriptor"/> or <see cref="LabelSignature"/>.</param>
    /// <returns><c>true</c> if the identities match; <c>false</c> for null or any other type.</returns>
    public bool MapsTo(Object obj)
    {
        if (obj == null) return false;
        if (obj is BodyShapeDescriptor)
        {
            var other = obj as BodyShapeDescriptor;
            return this.ID.MapsTo(other.ID);
        }
        else if (obj is LabelSignature)
        {
            var other = obj as LabelSignature;
            return other.Category == ID.Category && other.Value == ID.Value;
        }
        else
        {
            return false;
        }
    }

    /// <summary>A <see cref="LabelSignature"/> with an associated <see cref="Priority"/>, used where descriptors are ranked.</summary>
    public class PrioritizedLabelSignature: LabelSignature
    {
        public int Priority { get; set; } = 0;

        /// <summary>Returns "Category: Value (Priority)".</summary>
        public override string ToString()
        {
            return ToSignatureString(Category, Value) + " (" + Priority + ")";
        }
    }

    /// <summary>The Category+Value identity of a body-shape descriptor, with value-equality and string-conversion helpers.</summary>
    public class LabelSignature
    {
        public string Category { get; set; } = "";
        public string Value { get; set; } = "";

        /// <summary>Determines whether this signature matches another descriptor or signature by Category+Value.</summary>
        /// <param name="obj">A <see cref="BodyShapeDescriptor"/> or <see cref="LabelSignature"/>.</param>
        /// <returns><c>true</c> if the identities match.</returns>
        public bool MapsTo(Object obj)
        {
            if (obj == null) return false;
            if (obj is BodyShapeDescriptor)
            {
                var other = obj as BodyShapeDescriptor;
                return this.MapsTo(other.ID);
            }
            else if (obj is LabelSignature)
            {
                var other = obj as LabelSignature;
                return other.Category == Category && other.Value == Value;
            }
            else
            {
                return false;
            }
        }
        /// <summary>Determines whether any signature in the collection matches this one.</summary>
        /// <param name="collection">Signatures to search.</param>
        /// <returns><c>true</c> if a match is present.</returns>
        public bool CollectionContainsThisDescriptor(IEnumerable<LabelSignature> collection)
        {
            foreach (var d in collection)
            {
                if (d.MapsTo(this))
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>Determines whether any descriptor in the collection matches this signature.</summary>
        /// <param name="collection">Descriptors to search.</param>
        /// <returns><c>true</c> if a match is present.</returns>
        public bool CollectionContainsThisDescriptor(IEnumerable<BodyShapeDescriptor> collection)
        {
            foreach (var d in collection)
            {
                if (d.MapsTo(this))
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>Returns "Category: Value".</summary>
        public override string ToString()
        {
            return ToSignatureString(Category, Value);
        }

        /// <summary>Formats a category/value pair as "Category: Value".</summary>
        /// <param name="category">The category.</param>
        /// <param name="value">The value.</param>
        /// <returns>The combined signature string.</returns>
        public static string ToSignatureString(string category, string value)
        {
            return category + ": " + value;
        }

        /// <summary>Parses a "Category: Value" string into a <see cref="LabelSignature"/>, logging an error on malformed input.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="descriptor">Receives the parsed signature (empty on failure).</param>
        /// <param name="logger">Logger for error reporting.</param>
        /// <returns><c>true</c> if parsing succeeded.</returns>
        public static bool FromString(string s, out LabelSignature descriptor, Logger logger)
        {
            descriptor = new();
            if (s == null) 
            {
                logger.LogError("Could not convert null string into a Body Shape Descriptor");
                return false; 
            }
            string[] split = s.Split(':');
            if (split.Length != 2)
            {
                logger.LogError("Could not convert \"" + s + "\" into a Body Shape Descriptor - Expected 'Category: Value' format");
                return false;
            }
            else
            {
                descriptor.Category = split[0].Trim();
                descriptor.Value = split[1].Trim();
                return true;
            }
        }

        /// <summary>Value equality by Category and Value.</summary>
        public override bool Equals(object? obj)
        {
            var otherDescriptor = obj as LabelSignature;
            return otherDescriptor != null && otherDescriptor.Category == Category && otherDescriptor.Value == Value;
        }

        /// <summary>Hash code derived from Category and Value (consistent with <see cref="Equals"/>).</summary>
        public override int GetHashCode()
        {
            return this.Category.GetHashCode() ^ this.Value.GetHashCode();
        }
    }

    /// <summary>Determines whether an NPC is permitted this descriptor, delegating to <see cref="BodyShapeDescriptorRules.NPCisValid"/>.</summary>
    /// <param name="npcInfo">The NPC under evaluation.</param>
    /// <param name="attributeGroups">Named attribute groups referenced by the rules.</param>
    /// <param name="attMatcher">Matcher for attribute rules.</param>
    /// <param name="bDetailedAttributeLogging">When true, produces verbose attribute logs.</param>
    /// <param name="reportStr">Receives a human-readable rejection reason or forced-attribute note.</param>
    /// <returns><c>true</c> if the NPC is permitted.</returns>
    public bool PermitNPC(NPCInfo npcInfo, HashSet<AttributeGroup> attributeGroups, AttributeMatcher attMatcher, bool bDetailedAttributeLogging, out string reportStr)
    {
        return AssociatedRules.NPCisValid(this, attributeGroups, npcInfo, attMatcher, bDetailedAttributeLogging, out reportStr);
    }

    /// <summary>Determines whether a set of descriptors satisfies a category→allowed-values match specification, under one of three match modes.</summary>
    /// <typeparam name="T">A descriptor label-signature type.</typeparam>
    /// <param name="DescriptorSet">Map of category to the set of acceptable values.</param>
    /// <param name="shapeDescriptors">The descriptors to test.</param>
    /// <param name="matchMode">All (every spec category must match), Any (any one matches), or Shared (every shared category must match).</param>
    /// <param name="firstMatch">Receives the first matching "Category: Value", when applicable.</param>
    /// <returns>Whether the descriptors satisfy the specification under <paramref name="matchMode"/>.</returns>
    public static bool DescriptorsMatch<T>(Dictionary<string, HashSet<string>> DescriptorSet, HashSet<T> shapeDescriptors, DescriptorMatchMode matchMode, out string firstMatch)
        where T : BodyShapeDescriptor.LabelSignature
    {
        firstMatch = "";
        if (!shapeDescriptors.Any())
        {
            return false;
        }

        var categoriesToMatch = shapeDescriptors.Select(x => x.Category).ToArray();

        foreach (var category in DescriptorSet.Keys)
        {
            if (!categoriesToMatch.Contains(category))
            {
                if (matchMode == DescriptorMatchMode.All)
                {
                    return false;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                var allowedMatches = DescriptorSet[category];
                var relevantDescriptors = shapeDescriptors.Where(x => x.Category == category).ToArray();
                bool currentCategoryMatched = false;
                foreach (var candidateDescriptor in relevantDescriptors)
                {
                    if (allowedMatches.Contains(candidateDescriptor.Value))
                    {
                        firstMatch = category + ": " + candidateDescriptor.Value;
                        currentCategoryMatched = true;
                        break;
                    }
                }
                
                if (currentCategoryMatched == true && matchMode == DescriptorMatchMode.Any)
                {
                    return true;
                }
                else if (currentCategoryMatched == false && (matchMode == DescriptorMatchMode.All || matchMode == DescriptorMatchMode.Shared))
                {
                    return false;
                }
            }
        }

        // at this point:
        // if matchMode == All, then all descriptors have been matched, so return true.
        // if matchMode == Shared, then all descriptors contained within shapeDescriptors have been matched, so return true
        // If matchMode = Any, then none of the possible descriptors have been matched, so return false;
        if (matchMode == DescriptorMatchMode.Any)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
}

/// <summary>How a descriptor set is matched against a specification.</summary>
public enum DescriptorMatchMode
{
    /// <summary>Match if any one category/value matches.</summary>
    Any,
    /// <summary>Match only if every category in the specification matches.</summary>
    All,
    /// <summary>Match only if every category shared between the spec and the descriptors matches.</summary>
    Shared
}