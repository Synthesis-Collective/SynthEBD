using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Noggog;
using System.Diagnostics;

/*
 * When adding a new type of ITypedNPCAttribute, don't forget to also include it in JSONhandler.AttributeConverter.ReadJson() so that it can be correctly deserialized
 */

namespace SynthEBD;

// Each NPCAttribute within a HashSet<NPC> Attribute is treated with OR logic; i.e. if an NPC matches ANY of the NPCAttributes, the NPCAttribute's parent object can be assigned to the NPC
/// <summary>
/// One NPC-matching condition: a set of typed sub-attributes combined with AND logic (an NPC matches
/// only if it matches every <see cref="ITypedNPCAttribute"/> in <see cref="SubAttributes"/>). Owners
/// combine collections of NPCAttribute with OR logic.
/// </summary>
[DebuggerDisplay("Attribute with {SubAttributes.Count} Sub-Attributes (AND logic)")]
public class NPCAttribute
{
    public HashSet<ITypedNPCAttribute> SubAttributes { get; set; } = new(); // AND Logic

    /// <summary>Value equality against another <see cref="NPCAttribute"/>.</summary>
    public override bool Equals(object? obj)
    {
        NPCAttribute otherAttribute = obj as NPCAttribute;
        return otherAttribute != null && this.Equals(otherAttribute);
    }

    /// <summary>Determines whether two NPCAttributes have the same set of sub-attributes.</summary>
    /// <param name="other">The attribute to compare against.</param>
    /// <returns><c>true</c> if the sub-attribute sets match.</returns>
    /// <remarks>Order-independent: uses <see cref="HashSet{T}.SetEquals"/>, which relies on each sub-attribute's value <c>Equals</c>/<c>GetHashCode</c>.</remarks>
    public bool Equals(NPCAttribute other)
    {
        return other != null && this.SubAttributes.SetEquals(other.SubAttributes);
    }

    /// <summary>Order-independent hash for a set (XOR-fold of element hashes), consistent with <see cref="HashSet{T}.SetEquals"/>.</summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="items">The elements to hash.</param>
    /// <returns>A hash identical for any two collections holding the same set of items.</returns>
    public static int OrderIndependentHash<T>(IEnumerable<T> items)
    {
        int hash = 0;
        foreach (var item in items)
        {
            hash ^= item?.GetHashCode() ?? 0;
        }
        return hash;
    }

    /// <summary>Order-independent hash over the sub-attributes (consistent with <see cref="Equals(NPCAttribute)"/>).</summary>
    public override int GetHashCode()
    {
        bool first = true;
        int hashCode = 0;
        foreach (var item in SubAttributes.OrderBy(x => x.ToString()).ToArray())
        {
            if (first)
            {
                first = false;
                hashCode = item.GetHashCode();
            }
            else
            {
                hashCode ^= item.GetHashCode();
            }
        }
        return hashCode;
    }

    /// <summary>Resolves an attribute group by label, preferring the main settings' groups (when override is enabled) and falling back to plugin-supplied definitions.</summary>
    /// <param name="label">The group label to find.</param>
    /// <param name="groupDefinitions">Plugin-supplied group definitions (fallback).</param>
    /// <param name="patcherState">Patcher state holding the main group definitions and the override flag.</param>
    /// <param name="logger">Logger used when the group cannot be found.</param>
    /// <returns>The matching <see cref="AttributeGroup"/>, or null if none is found.</returns>
    public static AttributeGroup GetAttributeGroupByLabel(string label, HashSet<AttributeGroup> groupDefinitions, PatcherState patcherState, Logger logger)
    {
        if (patcherState.GeneralSettings.OverwritePluginAttGroups)
        {
            var matchedMainGroup = patcherState.GeneralSettings.AttributeGroups.Where(x => x.Label == label).FirstOrDefault();
            if (matchedMainGroup != null)
            {
                return matchedMainGroup;
            }
        }

        // fall back to plugin-supplied group definitions if necessary
        var matchedPluginGroup = groupDefinitions.Where(x => x.Label == label).FirstOrDefault();
        if (matchedPluginGroup != null)
        {
            return matchedPluginGroup;
        }
        logger.LogMessage("Could not find Attribute Group " + label + " in any group definition");
        return null;
    }

    /// <summary>Deep-clones an NPCAttribute, cloning each sub-attribute via the typed <see cref="CloneAsNew(ITypedNPCAttribute)"/> factory.</summary>
    /// <param name="input">The attribute to clone.</param>
    /// <returns>A new NPCAttribute with cloned sub-attributes.</returns>
    public static NPCAttribute CloneAsNew(NPCAttribute input)
    {
        NPCAttribute output = new NPCAttribute();
        foreach (var subAttribute in input.SubAttributes)
        {
            output.SubAttributes.Add(CloneAsNew(subAttribute));
        }
        return output;
    }

    /// <summary>Renders the attribute as "{sub AND sub AND ...}" for logs.</summary>
    /// <param name="bDetailedAttributes">When true, resolves FormKeys to names/EditorIDs.</param>
    /// <param name="linkCache">Link cache for name resolution.</param>
    /// <returns>A human-readable representation.</returns>
    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        return "{" + string.Join(" AND ", SubAttributes.Select(x => x.ToLogString(bDetailedAttributes, linkCache))) + "}";
    }

    /// <summary>Type-dispatching factory that clones any <see cref="ITypedNPCAttribute"/> by delegating to the concrete type's <c>CloneAsNew</c>.</summary>
    /// <param name="inputInterface">The sub-attribute to clone.</param>
    /// <returns>A new sub-attribute of the same concrete type, or null for an unknown type.</returns>
    /// <remarks>Must be kept in sync with <see cref="NPCAttributeType"/> and the JSON attribute converter whenever a new attribute type is added (see the file header comment).</remarks>
    public static ITypedNPCAttribute CloneAsNew(ITypedNPCAttribute inputInterface)
    {
        switch(inputInterface.Type)
        {
            case NPCAttributeType.Class: return NPCAttributeClass.CloneAsNew((NPCAttributeClass)inputInterface);
            case NPCAttributeType.Custom: return NPCAttributeCustom.CloneAsNew((NPCAttributeCustom)inputInterface);
            case NPCAttributeType.FaceTexture: return NPCAttributeFaceTexture.CloneAsNew((NPCAttributeFaceTexture)inputInterface);
            case NPCAttributeType.Faction: return NPCAttributeFactions.CloneAsNew((NPCAttributeFactions)inputInterface);
            case NPCAttributeType.Group: return NPCAttributeGroup.CloneAsNew((NPCAttributeGroup)inputInterface);
            case NPCAttributeType.Keyword: return NPCAttributeKeyword.CloneAsNew((NPCAttributeKeyword)inputInterface);
            case NPCAttributeType.Misc: return NPCAttributeMisc.CloneAsNew((NPCAttributeMisc)inputInterface);
            case NPCAttributeType.Mod: return NPCAttributeMod.CloneAsNew((NPCAttributeMod)inputInterface);
            case NPCAttributeType.NPC: return NPCAttributeNPC.CloneAsNew((NPCAttributeNPC)inputInterface);
            case NPCAttributeType.Race: return NPCAttributeRace.CloneAsNew((NPCAttributeRace)inputInterface);
            case NPCAttributeType.VoiceType: return NPCAttributeVoiceType.CloneAsNew((NPCAttributeVoiceType)inputInterface);
            default: return null;
        }
    }

    // Grouped Sub Attributes get merged together. E.g:
    // Parent has attributes (A && B) || (C && D)
    // Child has attributes (E && F) || (G && H)
    // After inheriting, child will have attributes (A && B && E && F) || (A && B && G && H) || (C && D && E && F) || (C && D && G && H)
    /// <summary>Merges two OR-sets of attributes by distributing AND across them: each parent OR-term is combined with each child OR-term into a new AND-combined term.</summary>
    /// <param name="inheritFrom">The parent attribute set.</param>
    /// <param name="inherits">The child attribute set.</param>
    /// <returns>The cartesian AND-merge; or whichever input is non-empty when the other is empty.</returns>
    public static HashSet<NPCAttribute> InheritAttributes(HashSet<NPCAttribute> inheritFrom, HashSet<NPCAttribute> inherits)
    {
        var mergedAttributes = new HashSet<NPCAttribute>();

        if (inheritFrom.Count > 0 && inherits.Count == 0)
        {
            return inheritFrom;
        }
        else if (inherits.Count > 0 && inheritFrom.Count == 0)
        {
            return inherits;
        }
        else
        {
            foreach (var childAttribute in inherits)
            {
                foreach (var parentAttribute in inheritFrom)
                {
                    var combinedAttribute = new NPCAttribute();

                    foreach (var subParentAttribute in parentAttribute.SubAttributes)
                    {
                        combinedAttribute.SubAttributes.Add(subParentAttribute);
                    }

                    foreach (var subChildAttribute in childAttribute.SubAttributes)
                    {
                        combinedAttribute.SubAttributes.Add(subChildAttribute);
                    }

                    mergedAttributes.Add(combinedAttribute);
                }
            }
        }

        return mergedAttributes;
    }

    /// <summary>Resolves a FormKey to a display string (preferring Name, then EditorID, then the raw key) for record types that expose a name.</summary>
    /// <typeparam name="T">A named major-record getter type.</typeparam>
    /// <param name="fk">The FormKey to resolve.</param>
    /// <param name="linkCache">Link cache for resolution.</param>
    /// <returns>The record's name/EditorID, or the FormKey string when unresolved.</returns>
    public static string FormKeyToLogStringNamed<T>(FormKey fk, ILinkCache linkCache) where T: class, IMajorRecordGetter, INamedGetter
    {
        string output = fk.ToString();
        if (linkCache.TryResolve<T>(fk, out var getter))
        {
            if (getter.Name != null && !string.IsNullOrWhiteSpace(getter.Name.ToString()))
            {
                output = getter.Name.ToString();
            }
            else if (getter.EditorID != null && !string.IsNullOrWhiteSpace(getter.EditorID))
            {
                output = getter.EditorID;
            }
        }
        return output;
    }

    /// <summary>Resolves a FormKey to its EditorID (or the raw key) for record types without a name.</summary>
    /// <typeparam name="T">A major-record getter type.</typeparam>
    /// <param name="fk">The FormKey to resolve.</param>
    /// <param name="linkCache">Link cache for resolution.</param>
    /// <returns>The record's EditorID, or the FormKey string when unresolved.</returns>
    public static string FormKeyToLogStringUnnamed<T>(FormKey fk, ILinkCache linkCache) where T : class, IMajorRecordGetter
    {
        string output = fk.ToString();
        if (linkCache.TryResolve<T>(fk, out var getter) && getter.EditorID != null && !string.IsNullOrWhiteSpace(getter.EditorID))
        {
            output = getter.EditorID;
        }
        return output;
    }
}

/// <summary>The kind of NPC property an <see cref="ITypedNPCAttribute"/> matches against (also the serialization discriminator).</summary>
public enum NPCAttributeType
{
    /// <summary>NPC Class record.</summary>
    Class,
    /// <summary>Custom record-path comparison.</summary>
    Custom,
    /// <summary>Head FaceTexture (texture set).</summary>
    FaceTexture,
    /// <summary>Faction membership (with rank range).</summary>
    Faction,
    /// <summary>A referenced attribute group.</summary>
    Group,
    /// <summary>Keyword.</summary>
    Keyword,
    /// <summary>Miscellaneous flags/traits.</summary>
    Misc,
    /// <summary>Mod provenance.</summary>
    Mod,
    /// <summary>A specific NPC.</summary>
    NPC,
    /// <summary>Race.</summary>
    Race,
    /// <summary>Voice type.</summary>
    VoiceType
}
/// <summary>Value kind for a <see cref="NPCAttributeCustom"/> condition.</summary>
public enum CustomAttributeType // moved outside of NPCAttributeCustom so that it can be visible to UC_NPCAttributeCustom's View binding
{
    /// <summary>String comparison.</summary>
    Text,
    /// <summary>Integer comparison.</summary>
    Integer,
    /// <summary>Decimal comparison.</summary>
    Decimal,
    /// <summary>Boolean comparison.</summary>
    Boolean,
    /// <summary>Record (FormKey) comparison.</summary>
    Record
}

/// <summary>How a matched attribute influences selection: restrict eligibility, force selection, or both.</summary>
public enum AttributeForcing
{
    /// <summary>The attribute only restricts which items may be selected.</summary>
    Restrict,
    /// <summary>If matched, forces selection of the carrying item.</summary>
    ForceIf,
    /// <summary>Both forces selection when matched and restricts otherwise.</summary>
    ForceIfAndRestrict
}

/// <summary>Matches NPCs whose Class record is among <see cref="FormKeys"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeClass : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Class;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Classes: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Classes: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeClass otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeClass other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^
            Type.GetHashCode() ^
            ForceMode.GetHashCode() ^
            Weighting.GetHashCode() ^
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }

    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation), safe for duplication in the UI.</summary>
    public static NPCAttributeClass CloneAsNew(NPCAttributeClass input)
    {
        var output = new NPCAttributeClass();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "Class: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringUnnamed<IClassGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "Class: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs by a custom record-path comparison: the value at <see cref="Path"/> compared (per <see cref="CustomType"/> and <see cref="Comparator"/>) against <see cref="ValueStr"/> or <see cref="ValueFKs"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeCustom : ITypedNPCAttribute
{
    public string Path { get; set; } = "";
    public string ValueStr { get; set; } = "";
    public HashSet<FormKey> ValueFKs { get; set; } = new();
    public CustomAttributeType CustomType { get; set; } = CustomAttributeType.Text;
    public string Comparator { get; set; }
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Custom;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    public FormKey ReferenceNPCFK { get; set; } // this is not used by the patcher but saving it avoids making the user reselect it in the UI
    public Type SelectedFormKeyType { get; set; } // this is not used by the patcher but saving it avoids making the user reselect it in the UI
    
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            return (Not ? "NOT " : "") + CustomType.ToString();
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeCustom otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.CustomType == otherTyped.CustomType
            && this.Path == otherTyped.Path
            && this.Comparator == otherTyped.Comparator
            && this.ValueStr == otherTyped.ValueStr
            && this.ValueFKs.SetEquals(otherTyped.ValueFKs);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeCustom other && Equals(other);

    public override int GetHashCode()
    {
        return Path.GetHashCode() ^
            ValueStr.GetHashCode() ^
            NPCAttribute.OrderIndependentHash(ValueFKs) ^
            CustomType.GetHashCode() ^
            (Comparator?.GetHashCode() ?? 0) ^
            Type.GetHashCode() ^
            ForceMode.GetHashCode() ^
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        if (CustomType == CustomAttributeType.Record && !ValueFKs.Any())
        {
            return true;
        }
        else if ((CustomType == CustomAttributeType.Integer || CustomType == CustomAttributeType.Decimal || CustomType == CustomAttributeType.Boolean) && ValueStr.IsNullOrWhitespace())
        {
            return true;
        }
        return false;
    }
    /// <summary>Returns a copy of the given attribute (deep-copies the Record FormKey set; copies the scalar value otherwise).</summary>
    public static NPCAttributeCustom CloneAsNew(NPCAttributeCustom input)
    {
        var output = new NPCAttributeCustom();
        output.CustomType = input.CustomType;
        output.ForceMode = input.ForceMode;
        output.Path = input.Path;
        output.Type = input.Type;
        if (input.CustomType == CustomAttributeType.Record)
        {
            output.ValueFKs = new HashSet<FormKey>();
            foreach (var fk in input.ValueFKs)
            {
                output.ValueFKs.Add(new FormKey(fk.ModKey, fk.ID));
            }
        }
        else
        {
            output.ValueStr = input.ValueStr;
        }
        output.Comparator = input.Comparator;
        output.Not = input.Not;
        output.ReferenceNPCFK = input.ReferenceNPCFK;
        output.SelectedFormKeyType = input.SelectedFormKeyType;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool _, ILinkCache __)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        logStr +=  "\tCustom ";
        switch(CustomType)
        {
            case CustomAttributeType.Integer: logStr += "Integer: " + Path + " " + Comparator + " " + ValueStr; break;
            case CustomAttributeType.Decimal: logStr += "Decimal: " + Path + " " + Comparator + " " + ValueStr; break;
            case CustomAttributeType.Boolean: logStr += "Boolean: " + Path + " " + Comparator + " " + ValueStr; break;
            case CustomAttributeType.Text: logStr += "Text: " + Path + " " + Comparator + " " + ValueStr; break;
            case CustomAttributeType.Record: logStr += "Record: " + Path + " " + Comparator + " " + String.Join(", ", ValueFKs.Select(x => x.ToString())); break; // probably not worth using a generic TryResolve (or using reflection to get type) just for logging
        }
        return logStr;
    }
}

/// <summary>Matches NPCs belonging to any faction in <see cref="FormKeys"/> within the <see cref="RankMin"/>–<see cref="RankMax"/> rank range.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeFactions : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public int RankMin { get; set; } = -1;
    public int RankMax { get; set; } = 100;
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Faction;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Factions: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Factions: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeFactions otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.RankMin == otherTyped.RankMin
            && this.RankMax == otherTyped.RankMax
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeFactions other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^ 
            RankMin.GetHashCode() ^
            RankMax.GetHashCode() ^
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }

    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeFactions CloneAsNew(NPCAttributeFactions input)
    {
        var output = new NPCAttributeFactions();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.RankMin = input.RankMin;
        output.RankMax = input.RankMax;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "Factions: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringNamed<IFactionGetter>(x, linkCache))) + "] Rank: " + RankMin + " - " + RankMax;
        }
        else
        {
            return logStr + "Factions: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "] Rank: " + RankMin + " - " + RankMax;
        }
    }
}

/// <summary>Matches NPCs whose head FaceTexture (texture set) is among <see cref="FormKeys"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeFaceTexture : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.FaceTexture;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Face Textures: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Face Textures: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeFaceTexture otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeFaceTexture other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^ 
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }

    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeFaceTexture CloneAsNew(NPCAttributeFaceTexture input)
    {
        var output = new NPCAttributeFaceTexture();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "Face Texture: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringUnnamed<ITextureSetGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "Face Texture: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs carrying any of the keywords in <see cref="FormKeys"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeKeyword : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Keyword;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Keywords: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Keywords: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeKeyword otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeKeyword other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^
            Type.GetHashCode() ^
            ForceMode.GetHashCode() ^
            Weighting.GetHashCode() ^
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }

    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeKeyword CloneAsNew(NPCAttributeKeyword input)
    {
        var output = new NPCAttributeKeyword();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "Keyword: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringUnnamed<IKeywordGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "Keyword: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs of any race in <see cref="FormKeys"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeRace : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Race;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Races: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Races: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeRace otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeRace other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^ 
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }
    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeRace CloneAsNew(NPCAttributeRace input)
    {
        var output = new NPCAttributeRace();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "Race: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringNamed<IRaceGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "Race: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs by miscellaneous flags/traits (unique, essential, protected, summonable, ghost, invulnerable) and optionally mood, aggression, and gender.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeMisc : ITypedNPCAttribute
{
    public ThreeWayState Unique { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Essential { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Protected { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Summonable { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Ghost { get; set; } = ThreeWayState.Ignore;
    public ThreeWayState Invulnerable { get; set; } = ThreeWayState.Ignore;

    public bool EvalMood { get; set; } = false;
    public Mood Mood { get; set; } = Mood.Neutral;
    public bool EvalAggression { get; set; } = false;
    public Aggression Aggression { get; set; } = Aggression.Unaggressive;
    public bool EvalGender { get; set; } = false;
    public Gender NPCGender { get; set; } = Gender.Female;

    public NPCAttributeType Type { get; set; } = NPCAttributeType.Misc;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;

    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            return (Not ? "NOT " : "") + "Miscellaneous Attributes";
        }
    }

    public bool IsBlank()
    {
        return (
            Unique == ThreeWayState.Ignore &&
            Essential == ThreeWayState.Ignore &&
            Protected == ThreeWayState.Ignore &&
            Summonable == ThreeWayState.Ignore &&
            Ghost == ThreeWayState.Ignore &&
            Invulnerable == ThreeWayState.Ignore &&
            !EvalAggression &&
            !EvalMood &&
            !EvalGender
            );
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeMisc otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.Unique == otherTyped.Unique
            && this.Essential == otherTyped.Essential
            && this.Protected == otherTyped.Protected
            && this.Summonable == otherTyped.Summonable
            && this.Ghost == otherTyped.Ghost
            && this.Invulnerable == otherTyped.Invulnerable
            && this.EvalMood == otherTyped.EvalMood
            && this.Mood == otherTyped.Mood
            && this.EvalAggression == otherTyped.EvalAggression
            && this.Aggression == otherTyped.Aggression
            && this.EvalGender == otherTyped.EvalGender
            && this.NPCGender == otherTyped.NPCGender;
    }

    public override bool Equals(object? obj) => obj is NPCAttributeMisc other && Equals(other);

    public override int GetHashCode()
    {
        return
            Unique.GetHashCode() ^
            Essential.GetHashCode() ^
            Protected.GetHashCode() ^
            Summonable.GetHashCode() ^
            Ghost.GetHashCode() ^
            Invulnerable.GetHashCode() ^
            EvalMood.GetHashCode() ^
            Mood.GetHashCode() ^
            EvalAggression.GetHashCode() ^
            Aggression.GetHashCode() ^
            EvalGender.GetHashCode() ^
            NPCGender.GetHashCode() ^
            Type.GetHashCode() ^
            ForceMode.GetHashCode() ^
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
}

    /// <summary>Returns a copy of the given attribute, including every evaluated flag/value (mood, aggression, gender) and the Not negation.</summary>
    public static NPCAttributeMisc CloneAsNew(NPCAttributeMisc input)
    {
        var output = new NPCAttributeMisc();
        output.Unique = input.Unique;
        output.Essential = input.Essential;
        output.Protected = input.Protected;
        output.Summonable = input.Summonable;
        output.Ghost = input.Ghost;
        output.Invulnerable = input.Invulnerable;
        output.EvalMood = input.EvalMood;
        output.Mood = input.Mood;
        output.EvalAggression = input.EvalAggression;
        output.Aggression = input.Aggression;
        output.EvalGender = input.EvalGender;
        output.NPCGender = input.NPCGender;

        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool _, ILinkCache __)
    {
        string output = "";
        if (Not)
        {
            output += "NOT ";
        }
        output += "Misc:";
        if (Unique != ThreeWayState.Ignore) { output += " [Unique: " + Unique.ToString() + "]"; }
        if (Essential != ThreeWayState.Ignore) { output += " [Essential: " + Essential.ToString() + "]"; }
        if (Protected != ThreeWayState.Ignore) { output += " [Protected: " + Protected.ToString() + "]"; }
        if (Summonable != ThreeWayState.Ignore) { output += " [Summonable: " + Summonable.ToString() + "]"; }
        if (Ghost != ThreeWayState.Ignore) { output += " [Ghost: " + Ghost.ToString() + "]"; }
        if (Invulnerable != ThreeWayState.Ignore) { output += " [Invulnerable: " + Invulnerable.ToString() + "]"; }
        if (EvalMood) { output += " [Mood: " + Mood.ToString() + "]"; }
        if (EvalAggression) { output += " [Aggression: " + Aggression.ToString() + "]"; }

        return output;
    }
}

/// <summary>Matches NPCs by mod provenance — created/patched/winning-override/winning-appearance from any of <see cref="ModKeys"/>, per <see cref="ModActionType"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeMod : ITypedNPCAttribute
{
    public HashSet<ModKey> ModKeys { get; set; } = new();
    public ModAttributeEnum ModActionType { get; set; } = ModAttributeEnum.PatchedBy;
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Mod;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (ModKeys.Any())
            {
                return (Not ? "NOT " : "") + "ModKeys: " + String.Join(", ", ModKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "ModKeys: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeMod otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.ModActionType == otherTyped.ModActionType
            && this.ModKeys.SetEquals(otherTyped.ModKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeMod other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(ModKeys) ^
            ModActionType.GetHashCode() ^ 
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !ModKeys.Any();
    }
    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeMod CloneAsNew(NPCAttributeMod input)
    {
        var output = new NPCAttributeMod();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.ModKeys = new HashSet<ModKey>(input.ModKeys);
        output.Not = input.Not;
        output.ModActionType = input.ModActionType;
        output.Weighting = input.Weighting;
        return output;
    }
    public string ToLogString(bool _, ILinkCache __)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        return logStr + "Mod: [" + string.Join(", ", ModKeys.Select(x => x.FileName.ToString())) + "]";
    }
}

/// <summary>Matches specific NPCs by FormKey (<see cref="FormKeys"/>).</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeNPC : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.NPC;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "NPCs: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "NPCs: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeNPC otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeNPC other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^ 
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }
    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeNPC CloneAsNew(NPCAttributeNPC input)
    {
        var output = new NPCAttributeNPC();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "NPC: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringNamed<INpcGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "NPC: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs whose voice type is among <see cref="FormKeys"/>.</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeVoiceType : ITypedNPCAttribute
{
    public HashSet<FormKey> FormKeys { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.VoiceType;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;
    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (FormKeys.Any())
            {
                return (Not ? "NOT " : "") + "Voice Types: " + String.Join(", ", FormKeys.Select(x => x.ToString()));
            }
            else
            {
                return (Not ? "NOT " : "") + "Voice Types: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeVoiceType otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.FormKeys.SetEquals(otherTyped.FormKeys);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeVoiceType other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(FormKeys) ^ 
            Type.GetHashCode() ^ 
            ForceMode.GetHashCode() ^ 
            Weighting.GetHashCode() ^ 
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !FormKeys.Any();
    }
    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeVoiceType CloneAsNew(NPCAttributeVoiceType input)
    {
        var output = new NPCAttributeVoiceType();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.FormKeys = new HashSet<FormKey>(input.FormKeys);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        if (bDetailedAttributes)
        {
            return logStr + "VoiceType: [" + string.Join(", ", FormKeys.Select(x => NPCAttribute.FormKeyToLogStringUnnamed<IVoiceTypeGetter>(x, linkCache))) + "]";
        }
        else
        {
            return logStr + "VoiceType: [" + string.Join(", ", FormKeys.Select(x => x.ToString())) + "]";
        }
    }
}

/// <summary>Matches NPCs that satisfy any of the attribute groups named in <see cref="SelectedLabels"/> (resolved by label at match time).</summary>
[DebuggerDisplay("{DebuggerString}")]
public class NPCAttributeGroup : ITypedNPCAttribute
{
    public HashSet<string> SelectedLabels { get; set; } = new();
    public NPCAttributeType Type { get; set; } = NPCAttributeType.Group;
    public AttributeForcing ForceMode { get; set; } = AttributeForcing.Restrict;
    public int Weighting { get; set; } = 1;
    public bool Not { get; set; } = false;

    [JsonIgnore]
    public string DebuggerString
    {
        get
        {
            if (SelectedLabels.Any())
            {
                return (Not ? "NOT " : "") + "Selected Groups: " + String.Join(", ", SelectedLabels);
            }
            else
            {
                return (Not ? "NOT " : "") + "Selected Groups: None";
            }
        }
    }

    public bool Equals(ITypedNPCAttribute other)
    {
        return other is NPCAttributeGroup otherTyped
            && this.Type == otherTyped.Type
            && this.Not == otherTyped.Not
            && this.ForceMode == otherTyped.ForceMode
            && this.Weighting == otherTyped.Weighting
            && this.SelectedLabels.SetEquals(otherTyped.SelectedLabels);
    }

    public override bool Equals(object? obj) => obj is NPCAttributeGroup other && Equals(other);

    public override int GetHashCode()
    {
        return NPCAttribute.OrderIndependentHash(SelectedLabels) ^
            Type.GetHashCode() ^
            ForceMode.GetHashCode() ^
            Weighting.GetHashCode() ^
            Not.GetHashCode();
    }

    public bool IsBlank()
    {
        return !SelectedLabels.Any();
    }
    /// <summary>Returns an independent copy of the given attribute (deep-copies its collection and copies the Not negation).</summary>
    public static NPCAttributeGroup CloneAsNew(NPCAttributeGroup input)
    {
        var output = new NPCAttributeGroup();
        output.ForceMode = input.ForceMode;
        output.Type = input.Type;
        output.SelectedLabels = new HashSet<string>(input.SelectedLabels);
        output.Not = input.Not;
        output.Weighting = input.Weighting;
        return output;
    }

    public string ToLogString(bool _, ILinkCache __)
    {
        string logStr = "";
        if (Not)
        {
            logStr += "NOT ";
        }

        return logStr + "Group: [" + string.Join(", ", SelectedLabels) + "]";
    }
}

/// <summary>
/// Common contract for the typed sub-attributes that make up an <see cref="NPCAttribute"/> — each matches
/// a specific NPC property (class, faction, keyword, race, mod provenance, etc.). Implementations supply
/// value equality, a hash, blank detection, a log rendering, and the shared forcing/weighting/negation knobs.
/// </summary>
public interface ITypedNPCAttribute
{
    /// <summary>The attribute kind (discriminator for serialization and dispatch).</summary>
    NPCAttributeType Type { get; set; }
    /// <summary>Value equality against another typed attribute of the same kind.</summary>
    bool Equals(ITypedNPCAttribute other);
    /// <summary>Hash consistent with <see cref="Equals(ITypedNPCAttribute)"/>.</summary>
    int GetHashCode();
    /// <summary>Whether this attribute is empty/unconfigured (and should be ignored or flagged).</summary>
    bool IsBlank();
    /// <summary>How a match forces/restricts selection.</summary>
    public AttributeForcing ForceMode { get; set; }
    /// <summary>Relative weight applied when this attribute participates in weighted selection.</summary>
    public int Weighting { get; set; }
    /// <summary>When true, the match condition is negated.</summary>
    public bool Not { get; set; }
    /// <summary>Renders the attribute for logs.</summary>
    public string ToLogString(bool bDetailedAttributes, ILinkCache linkCache);
    /// <summary>Debugger display string.</summary>
    public string DebuggerString { get; }
}

/// <summary>A named, reusable set of <see cref="NPCAttribute"/>s referenced by label (via <see cref="NPCAttributeGroup"/>).</summary>
[DebuggerDisplay("{Label}")]
public class AttributeGroup : IHasLabel
{
    public string Label { get; set; } = "";
    public HashSet<NPCAttribute> Attributes { get; set; } = new();
}

/// <summary>A tri-state flag filter: ignore the flag, require it set, or require it unset.</summary>
public enum ThreeWayState
{
    /// <summary>Do not consider this flag.</summary>
    Ignore,
    /// <summary>Require the flag to be set.</summary>
    Is,
    /// <summary>Require the flag to be unset.</summary>
    IsNot
}

/// <summary>How a <see cref="NPCAttributeMod"/> relates an NPC record to a mod.</summary>
public enum ModAttributeEnum
{
    /// <summary>The NPC record was originally created by the mod.</summary>
    CreatedBy,
    /// <summary>The mod provides an override of the NPC record.</summary>
    PatchedBy,
    /// <summary>The mod is the winning override of the NPC record.</summary>
    WinningOverrideIsFrom,
    /// <summary>The mod is the winning appearance (visual) override of the NPC record.</summary>
    WinningAppearanceIsFrom
}