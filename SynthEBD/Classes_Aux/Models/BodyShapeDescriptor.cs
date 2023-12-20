using Newtonsoft.Json;
using Synthesis.Bethesda.Execution.DotNet;
using System.Diagnostics;

namespace SynthEBD;

[DebuggerDisplay("{ID.ToString()}")]
public class BodyShapeDescriptor : IHasLabel
{
    public BodyShapeDescriptor()
    {
        // empty - used for everything besides backward-compatibile deserialization
    }

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
    public string CategoryDescription { get; set; } // this will be redundant for Descriptors with the same Category. Not worth breaking backwards compatibility to make BodyShapeDescriptor class nested like VM_BodyShapeDescriptor is.
    public string ValueDescription { get; set; } 

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

    public class PrioritizedLabelSignature: LabelSignature
    {
        public int Priority { get; set; } = 0;

        public override string ToString()
        {
            return ToSignatureString(Category, Value) + " (" + Priority + ")";
        }
    }

    public class LabelSignature
    {
        public string Category { get; set; } = "";
        public string Value { get; set; } = "";

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
        public override string ToString()
        {
            return ToSignatureString(Category, Value);
        }

        public static string ToSignatureString(string category, string value)
        {
            return category + ": " + value;
        }

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

        public override bool Equals(object? obj)
        {
            var otherDescriptor = obj as LabelSignature;
            return otherDescriptor != null && otherDescriptor.Category == Category && otherDescriptor.Value == Value;
        }

        public override int GetHashCode()
        {
            return this.Category.GetHashCode() ^ this.Value.GetHashCode();
        }
    }

    public bool PermitNPC(NPCInfo npcInfo, HashSet<AttributeGroup> attributeGroups, AttributeMatcher attMatcher, bool bDetailedAttributeLogging, out string reportStr)
    {
        return AssociatedRules.NPCisValid(this, attributeGroups, npcInfo, attMatcher, bDetailedAttributeLogging, out reportStr);
    }

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

public enum DescriptorMatchMode
{
    Any,
    All,
    Shared
}