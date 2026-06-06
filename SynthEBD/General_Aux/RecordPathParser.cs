using System.Reflection;
using System.Text.RegularExpressions;
using DynamicExpresso;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using System.Linq.Expressions;
using Loqui;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD;

/// <summary>
/// Metadata about an object located by <see cref="RecordPathParser.GetObjectAtPath(dynamic, IMajorRecordGetter, string, Dictionary{string, dynamic}, ILinkCache, bool, string, out dynamic, out ObjectInfo)"/>:
/// whether it is a record (has a FormKey), its resolved type/registration, and its index within a parent array.
/// </summary>
public class ObjectInfo
{
    /// <summary>True if the located object is (or resolves to) a record with a FormKey.</summary>
    public bool HasFormKey { get; set; } = false;
    /// <summary>True if the path terminated at a form link that is null/unresolvable.</summary>
    public bool IsNullFormLink { get; set; } = false;
    /// <summary>The Loqui/record type of the located object, when known.</summary>
    public Type RecordType { get; set; } = null;
    /// <summary>The Loqui registration for <see cref="RecordType"/>, when the object is a record.</summary>
    public ILoquiRegistration LoquiRegistration { get; set; } = null;
    /// <summary>The FormKey of the located record (default when not a record).</summary>
    public FormKey RecordFormKey { get; set; } = new();
    /// <summary>Index of the object within its parent array, when reached through an array specifier.</summary>
    public int? IndexInParentArray { get; set; } = null;
}
/// <summary>
/// Traverses and mutates Mutagen records via a string "record path" DSL (e.g.
/// <c>WornArmor.Armature[BodyTemplate...HasFlag(...) &amp;&amp; MatchRace(...)].SkinTexture.Male.Diffuse.GivenPath</c>).
/// Walks properties by reflection, supports array indexing by position, wildcard, or a boolean condition,
/// and evaluates those conditions with DynamicExpresso — including the custom <c>MatchRace</c> and
/// <c>PatchableRaces</c> operators. This is the engine behind the destination paths in
/// <see cref="FilePathDestinationMap"/> and the record output in <c>RecordGenerator</c>.
/// </summary>
public class RecordPathParser
{
    private readonly IEnvironmentStateProvider _environmentProvider; 
    private readonly Logger _logger;
    private readonly PatchableRaceResolver _raceResolver;
    /// <summary>Creates the parser.</summary>
    /// <param name="environmentProvider">Supplies the link cache for resolving records during traversal.</param>
    /// <param name="logger">Logger for path-resolution errors.</param>
    /// <param name="raceResolver">Supplies the patchable-race set for the <c>PatchableRaces</c> condition operator.</param>
    public RecordPathParser(IEnvironmentStateProvider environmentProvider, Logger logger, PatchableRaceResolver raceResolver)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _raceResolver = raceResolver;
    }

    private static readonly Interpreter _dynExpInterpreter = CreateInterpreter();
    /// <summary>Builds the shared DynamicExpresso interpreter, registering all exported Skyrim types so conditions can reference enums/flags like <c>BipedObjectFlag</c>.</summary>
    /// <returns>A configured interpreter used to compile array-condition expressions.</returns>
    private static Interpreter CreateInterpreter()
    {
        var interp = new Interpreter();
        var skyrimTypes = typeof(Mutagen.Bethesda.Skyrim.BipedObjectFlag).Assembly
            .GetExportedTypes()
            .Select(t => new ReferenceType(t));
        interp.Reference(skyrimTypes);
        return interp;
    }

    private static readonly Dictionary<string, Lambda> _lambdaCache = new();

    /// <summary>Compiles (and caches) a boolean condition expression for the given parameter types and evaluates it.</summary>
    /// <param name="expression">The condition text, with parameters referenced as <c>_0</c>, <c>_1</c>, ...</param>
    /// <param name="parameters">The actual parameter values, in order.</param>
    /// <returns>The boolean result of evaluating the expression.</returns>
    /// <remarks>Lambdas are cached by expression text plus parameter type signature. The cache is a plain dictionary — see review notes on thread-safety.</remarks>
    private static bool EvalBoolExpression(string expression, List<dynamic> parameters)
    {
        var dynParams = new Parameter[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            object val = (object)parameters[i];
            dynParams[i] = new Parameter("_" + i, val.GetType(), val);
        }

        // Build a cache key from the expression text + the parameter type signature
        string cacheKey = expression;
        for (int i = 0; i < dynParams.Length; i++)
        {
            cacheKey += "|" + dynParams[i].Type.FullName;
        }

        if (!_lambdaCache.TryGetValue(cacheKey, out Lambda lambda))
        {
            lambda = _dynExpInterpreter.Parse(expression, dynParams);
            _lambdaCache[cacheKey] = lambda;
        }

        return (bool)lambda.Invoke(dynParams);
    }

    //note: To allow the most flexibility in alternative usages, rootRecord can be any IMajorRecordGetter, but in SynthEBD it should always be the root INpcGetter.
    /// <summary>Resolves the single object at a record path relative to <paramref name="rootObj"/> (convenience overload that discards the <see cref="ObjectInfo"/>).</summary>
    /// <param name="rootObj">The object the path is traversed from (may be a sub-object of <paramref name="rootRecord"/>).</param>
    /// <param name="rootRecord">The root record of the whole tree (used to evaluate race/condition operators).</param>
    /// <param name="relativePath">The dot/bracket-delimited path to traverse.</param>
    /// <param name="objectCache">Per-traversal cache of already-resolved sub-paths.</param>
    /// <param name="linkCache">Link cache for resolving sub-records.</param>
    /// <param name="suppressMissingPathErrors">When true, missing path segments are not logged as errors.</param>
    /// <param name="errorCaption">Caption prefixed to any logged errors.</param>
    /// <param name="outputObj">Receives the resolved object, or null.</param>
    /// <returns><c>true</c> if the path resolved to an object.</returns>
    public bool GetObjectAtPath(dynamic rootObj, IMajorRecordGetter rootRecord, string relativePath, Dictionary<string, dynamic> objectCache, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, out dynamic outputObj) // rootObj is the object relative to which the path is to be traversed. rootRecord is the parent record of the entire tree - this may be the same as rootObj, but rootObj may be a sub-object of rootRecord
    {
        return GetObjectAtPath(rootObj, rootRecord, relativePath, objectCache, linkCache, suppressMissingPathErrors, errorCaption, out outputObj, out ObjectInfo _);
    }

    /// <summary>Resolves the single object at a record path relative to <paramref name="rootObj"/>, reflecting through properties and resolving array specifiers and sub-records, and reports metadata about the result.</summary>
    /// <param name="rootObj">The object the path is traversed from (may be a sub-object of <paramref name="rootRecord"/>).</param>
    /// <param name="rootRecord">The root record of the whole tree (used to evaluate race/condition operators).</param>
    /// <param name="relativePath">The dot/bracket-delimited path to traverse; an empty path returns <paramref name="rootObj"/> itself.</param>
    /// <param name="objectCache">Per-traversal cache of already-resolved sub-paths (populated as it walks).</param>
    /// <param name="linkCache">Link cache for resolving sub-records.</param>
    /// <param name="suppressMissingPathErrors">When true, missing path segments are not logged as errors.</param>
    /// <param name="errorCaption">Caption prefixed to any logged errors.</param>
    /// <param name="outputObj">Receives the resolved object, or null.</param>
    /// <param name="outputObjInfo">Receives metadata (record-ness, type, FormKey, array index) for the final object.</param>
    /// <returns><c>true</c> if the path resolved to an object.</returns>
    /// <remarks>Null form links are intentionally not cached, so upstream callers can distinguish "resolved" from "present but null".</remarks>
    public bool GetObjectAtPath(dynamic rootObj, IMajorRecordGetter rootRecord, string relativePath, Dictionary<string, dynamic> objectCache, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, out dynamic outputObj, out ObjectInfo outputObjInfo) // rootObj is the object relative to which the path is to be traversed. rootRecord is the parent record of the entire tree - this may be the same as rootObj, but rootObj may be a sub-object of rootRecord
    {
        outputObj = null;
        outputObjInfo = new ObjectInfo();

        int? indexInParentArray = null;
        bool isRecord = false;
        bool isNullFormLink = false;
        Type objectType = null;
        FormKey recordFormKey = new FormKey();

        if (rootObj == null)
        {
            return false;
        }

        if (relativePath == "")
        {
            outputObj = rootObj;
            if (ObjectHasFormKey(rootObj))
            {
                outputObjInfo.HasFormKey = true;
                if (TryGetRegister(outputObj, out objectType))
                {
                    outputObjInfo.RecordType = objectType;
                }
            }

            return true;
        }
        if (!objectCache.ContainsKey("")) { objectCache.Add("", rootObj); }

        string[] splitPath = SplitPath(relativePath);
        dynamic currentObj = rootObj;

        for (int i = 0; i < splitPath.Length; i++)
        {
            // reinitialize all output object info so that only the info from splitPath[last] is returned 
            indexInParentArray = null;
            isRecord = false;
            isNullFormLink = false;
            objectType = null;
            recordFormKey = new FormKey();

            if (currentObj == null)
            {
                return false;
            }

            // check object cache to see if the given object has already been resolved
            string concatPath = RecordGenerator.BuildPath(splitPath.ToList().GetRange(0, i + 1)); 
            if (objectCache.ContainsKey(concatPath))
            {
                currentObj = objectCache[concatPath];
                continue;
            }

            // otherwise search for the given value via Reflection
            string currentSubPath = splitPath[i];

            // handle arrays
            if (PathIsArray(currentSubPath, out string arrIndex))
            {
                // special case of UI transition where user deletes the array index
                if (currentSubPath == "[]") { return false; }

                if (!GetArrayObjectAtIndex(currentObj, arrIndex, rootRecord, linkCache, suppressMissingPathErrors, errorCaption, out currentObj, out indexInParentArray))
                {
                    return false;
                }
            }
            else if (!GetSubObject(currentObj, currentSubPath, out currentObj))
            {
                return false;
            }

            // if the current property is another record, resolve it to traverse
            IMajorRecordGetter subRecordGetter = null;
            if (ObjectHasFormKey(currentObj, out FormKey? subrecordFK))
            {
                if (!subrecordFK.Value.IsNull && TryGetRegister(currentObj, out objectType) && linkCache.TryResolve(subrecordFK.Value, objectType, out subRecordGetter))
                {
                    isRecord = true;
                    recordFormKey = subrecordFK.Value;
                    currentObj = subRecordGetter;
                }
                else
                {
                    isNullFormLink = true;
                }
            }
        }

        if (!objectCache.ContainsKey(relativePath) && !isNullFormLink) // don't cache null formlinks - improves performance of upstream code because it doesn't have to check for null formlink before deciding to add vs. overwrite existing dictionary key
        {
            objectCache.Add(relativePath, currentObj);
        }

        outputObjInfo.HasFormKey = isRecord;
        outputObjInfo.IsNullFormLink = isNullFormLink;
        outputObjInfo.RecordFormKey = recordFormKey;
        outputObjInfo.RecordType = objectType;
        outputObjInfo.IndexInParentArray = indexInParentArray;

        if (isRecord)
        {
            outputObjInfo.LoquiRegistration = LoquiRegistration.GetRegister(objectType);
        }

        outputObj = currentObj;
        return true;
    }

    /// <summary>Resolves a record path that may fan out to multiple objects (via a wildcard <c>[*]</c> or multi-match condition), accumulating every matching object.</summary>
    /// <param name="rootObj">The object the path is traversed from.</param>
    /// <param name="rootRecord">The root record of the whole tree.</param>
    /// <param name="relativePath">The dot/bracket-delimited path to traverse; an empty path yields <paramref name="rootObj"/> alone.</param>
    /// <param name="objectCache">Per-traversal cache of resolved sub-paths.</param>
    /// <param name="linkCache">Link cache for resolving sub-records.</param>
    /// <param name="suppressMissingPathErrors">When true, missing path segments are not logged as errors.</param>
    /// <param name="errorCaption">Caption prefixed to any logged errors.</param>
    /// <param name="outputObjectCollection">Receives all resolved objects (de-duplicated).</param>
    /// <returns><c>true</c> if at least one object resolved.</returns>
    /// <remarks>When a path segment fans out, the remaining sub-path is resolved recursively against each branch and the results unioned.</remarks>
    public bool GetObjectCollectionAtPath(dynamic rootObj, IMajorRecordGetter rootRecord, string relativePath, Dictionary<string, dynamic> objectCache, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, List<dynamic> outputObjectCollection)
    {
        if (rootObj == null)
        {
            return false;
        }

        if (relativePath == "")
        {
            outputObjectCollection.Add(rootObj);
            return true;
        }

        string[] splitPath = SplitPath(relativePath);
        dynamic currentObj = rootObj;

        for (int i = 0; i < splitPath.Length; i++)
        {
            if (currentObj == null)
            {
                return false;
            }

            // check object cache to see if the given object has already been resolved
            string concatPath = RecordGenerator.BuildPath(splitPath.ToList().GetRange(0, i + 1));
            if (objectCache.ContainsKey(concatPath))
            {
                currentObj = objectCache[concatPath];
                continue;
            }

            // otherwise search for the given value via Reflection
            string currentSubPath = splitPath[i];

            // handle arrays
            if (PathIsArray(currentSubPath, out string arrIndex))
            {
                // special case of UI transition where user deletes the array index
                if (currentSubPath == "[]") { return false; }

                if (!GetArrayObjectCollectionAtIndex(currentObj, arrIndex, rootRecord, linkCache, suppressMissingPathErrors, errorCaption, outputObjectCollection) || !outputObjectCollection.Any())
                {
                    return false;
                }
            }
            else if (!GetSubObject(currentObj, currentSubPath, out currentObj))
            {
                return false;
            }

            // traverse all subObjects if any
            if (outputObjectCollection.Any())
            {
                var tmpCollection = new List<dynamic>();
                var subPath = relativePath.Remove(0, concatPath.Length);

                if (subPath.StartsWith('.'))
                {
                    subPath = subPath.Remove(0, 1);
                }

                if (!subPath.Any())
                {
                    return true;
                }

                foreach (var obj in outputObjectCollection)
                {
                    List<dynamic> collectionSubObjects = new List<dynamic>();
                    if (GetObjectCollectionAtPath(obj, rootRecord, subPath, new Dictionary<string, dynamic>(), linkCache, suppressMissingPathErrors, errorCaption, collectionSubObjects))
                    {
                        foreach (var subObj in collectionSubObjects)
                        {
                            if (!tmpCollection.Contains(subObj))
                            {
                                tmpCollection.Add(subObj);
                            }
                        }
                    }
                }

                outputObjectCollection.Clear();
                outputObjectCollection.AddRange(tmpCollection);

                return outputObjectCollection.Any();
            }

            // if the current property is another record, resolve it to traverse
            if (ObjectHasFormKey(currentObj, out FormKey? subrecordFK))
            {
                if (subrecordFK != null && !subrecordFK.Value.IsNull && linkCache.TryResolve(subrecordFK.Value, (Type)currentObj.Type, out var subRecordGetter))
                {
                    currentObj = subRecordGetter;
                }
            }
        }

        if (!objectCache.ContainsKey(relativePath))
        {
            objectCache.Add(relativePath, currentObj);
        }

        outputObjectCollection.Add(currentObj);
        return outputObjectCollection.Any();
    }

    /// <summary>Walks a path segment-by-segment and reports the deepest record encountered along it, plus the remaining sub-path from that record to the target.</summary>
    /// <param name="rootGetter">The record to start from.</param>
    /// <param name="path">The full path to walk.</param>
    /// <param name="linkCache">Link cache for resolving sub-records.</param>
    /// <param name="suppressMissingPathErrors">When true, missing path segments are not logged as errors.</param>
    /// <param name="errorCaption">Caption prefixed to any logged errors.</param>
    /// <param name="parentRecordGetter">Receives the nearest (deepest) record getter along the path.</param>
    /// <param name="relativePath">Receives the path from <paramref name="parentRecordGetter"/> to the target.</param>
    /// <returns><c>true</c> if the whole path resolved; <c>false</c> if any segment failed.</returns>
    /// <remarks>Used so the patcher can override the nearest concrete record rather than a non-record sub-object.</remarks>
    public bool GetNearestParentGetter(IMajorRecordGetter rootGetter, string path, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, bool suppressMissingPathErrors, string errorCaption, out IMajorRecordGetter parentRecordGetter, out string relativePath)
    {
        string[] splitPath = SplitPath(path);
        dynamic currentObj = rootGetter;
        parentRecordGetter = null;
        relativePath = "";
        var objectCache = new Dictionary<string, dynamic>();
        objectCache.Add("", rootGetter);

        for (int i = 0; i < splitPath.Length; i++)
        {
            if (GetObjectAtPath(currentObj, rootGetter, splitPath[i], objectCache, linkCache, suppressMissingPathErrors, errorCaption, out currentObj, out ObjectInfo currentObjInfo))
            {
                if (currentObjInfo.HasFormKey)
                {
                    parentRecordGetter = currentObj;
                    relativePath = "";
                }
                else
                {
                    relativePath += splitPath[i];
                    if (i < splitPath.Length - 1)
                    {
                        relativePath += ".";
                    }
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }
    /// <summary>Resolves the single array element selected by an index specifier, discarding the element's index.</summary>
    /// <param name="currentObj">The array/list object.</param>
    /// <param name="arrIndex">The specifier inside the brackets: a number or a boolean condition.</param>
    /// <param name="rootRecord">Root record for condition evaluation.</param>
    /// <param name="linkCache">Link cache for resolving records.</param>
    /// <param name="suppressMissingPathErrors">When true, suppresses missing-element error logging.</param>
    /// <param name="errorCaption">Caption for logged errors.</param>
    /// <param name="outputObj">Receives the selected element, or null.</param>
    /// <returns><c>true</c> if an element was selected.</returns>
    private bool GetArrayObjectAtSpecifier(dynamic currentObj, string arrIndex, IMajorRecordGetter rootRecord, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, out dynamic outputObj)
    {
        return GetArrayObjectAtIndex(currentObj, arrIndex, rootRecord, linkCache, suppressMissingPathErrors, errorCaption, out outputObj, out int? _);
    }
    /// <summary>Selects the array element identified by <paramref name="arrIndex"/> — either a numeric position or a boolean condition matched against each element.</summary>
    /// <param name="currentObj">The array/list object (must be an <see cref="IReadOnlyList{T}"/> of dynamic).</param>
    /// <param name="arrIndex">A numeric index, or a condition expression.</param>
    /// <param name="rootRecord">Root record for condition evaluation.</param>
    /// <param name="linkCache">Link cache for resolving records.</param>
    /// <param name="suppressMissingPathErrors">When true, suppresses missing-element error logging.</param>
    /// <param name="errorCaption">Caption for logged errors.</param>
    /// <param name="outputObj">Receives the selected element, or null.</param>
    /// <param name="indexInParent">Receives the element's index within the array.</param>
    /// <returns><c>true</c> if an element was selected.</returns>
    private bool GetArrayObjectAtIndex(dynamic currentObj, string arrIndex, IMajorRecordGetter rootRecord, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, out dynamic outputObj, out int? indexInParent)
    {
        outputObj = null;
        indexInParent = null;

        var collectionObj = currentObj as IReadOnlyList<dynamic>;
        if (collectionObj == null)
        {
            _logger.LogError("Could not cast " + currentObj.GetType() + "as an XXX");
            return false;
        }

        //if array index is numeric
        if (int.TryParse(arrIndex, out int iIndex))
        {
            if (iIndex < 0 || iIndex < collectionObj.Count())
            {
                outputObj = collectionObj.ElementAt(iIndex);
                indexInParent = iIndex;
            }
            else
            {
                if (!suppressMissingPathErrors)
                {
                    _logger.LogError(errorCaption + ": Could not get object at [" + arrIndex + "] because the " + currentObj.GetType() + " does not have an element at this index.");
                }
                return false;
            }
        }

        // if array index specifies object by property, figure out which index is the right one
        else
        {
            if (!ChooseWhichArrayObject(collectionObj, arrIndex, rootRecord, linkCache, suppressMissingPathErrors, errorCaption, out outputObj, out indexInParent))
            {
                if (!suppressMissingPathErrors)
                {
                    _logger.LogError(errorCaption + ": Could not get object at [" + arrIndex + "] because " + currentObj.GetType() + " does not have an element that matches this condition.");
                }
                return false;
            }
        }

        return true;
    }

    /// <summary>Selects array elements by specifier into a collection: a numeric index (one element), <c>*</c> (all elements), or a boolean condition (all matches).</summary>
    /// <param name="currentObj">The array/list object.</param>
    /// <param name="arrIndex">A numeric index, <c>*</c>, or a condition expression.</param>
    /// <param name="rootRecord">Root record for condition evaluation.</param>
    /// <param name="linkCache">Link cache for resolving records.</param>
    /// <param name="suppressMissingPathErrors">When true, suppresses missing-element error logging.</param>
    /// <param name="errorCaption">Caption for logged errors.</param>
    /// <param name="outputObjectCollection">Receives the selected elements (cleared first).</param>
    /// <returns><c>true</c> if at least one element was selected.</returns>
    private bool GetArrayObjectCollectionAtIndex(dynamic currentObj, string arrIndex, IMajorRecordGetter rootRecord, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, List<dynamic> outputObjectCollection)
    {
        outputObjectCollection.Clear();

        var collectionObj = currentObj as IReadOnlyList<dynamic>;
        if (collectionObj == null)
        {
            _logger.LogError("Could not cast " + currentObj.GetType() + "as an IReadOnlyList");
            return false;
        }

        //if array index is numeric
        if (int.TryParse(arrIndex, out int iIndex))
        {
            if (iIndex < 0 || iIndex < collectionObj.Count())
            {
                outputObjectCollection.Add(collectionObj.ElementAt(iIndex));
                return true;
            }
            else
            {
                if (!suppressMissingPathErrors)
                {
                    string currentSubPath = "[" + arrIndex + "]";
                    _logger.LogError("Could not get object at " + currentSubPath + " because the " + currentObj.GetType() + " does not have an element at index " + iIndex);
                }
                return false;
            }
        }
        else if (arrIndex == "*")
        {
            foreach (var arrElement in collectionObj)
            {
                outputObjectCollection.Add(arrElement);
            }
            return true;
        }

        // if array index specifies object by property, figure out which index is the right one
        else
        {
            if (ChooseSelectedArrayObjects(collectionObj, rootRecord, arrIndex, linkCache, suppressMissingPathErrors, errorCaption, outputObjectCollection))
            {
                return true;
            }
            else
            {
                if (!suppressMissingPathErrors)
                {
                    string currentSubPath = "[" + arrIndex + "]";
                    _logger.LogError("Could not get object at " + currentSubPath + " because " + currentObj.GetType() + " does not have an element that matches condition: " + arrIndex);
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Parses and represents a single array-index condition from the path DSL — its subject path, the
    /// expression template substituted by a parameter placeholder, the comparison/match condition, and
    /// any special handling (<c>Invoke</c>, <c>MatchRace</c>, <c>PatchableRaces</c>).
    /// </summary>
    private class ArrayPathCondition
    {
        /// <summary>Parses a single condition string into its path, replacer template, comparator, and match condition.</summary>
        /// <param name="strIndex">The raw condition text (one term of an array specifier).</param>
        /// <param name="parsed">Receives whether parsing succeeded.</param>
        private ArrayPathCondition(string strIndex, out bool parsed)
        {
            parsed = false;

            strIndex = RemovePairedParens(strIndex);
            strIndex = TrimParens(strIndex);

            if (strIndex.Contains("Invoke:") && !ReplaceUncomparedInvokeCalls(strIndex, out strIndex))
            {
                return;
            }

            int sepIndex = -1;
            Comparator = "";

            strIndex = strIndex.Replace("=>", "{LAMBDA}"); // preserve lambda operator

            foreach (var comparator in Comparators)
            {
                if (strIndex.Contains(comparator))
                {
                    sepIndex = strIndex.LastIndexOf(comparator);
                    Comparator = comparator;
                    break;
                }
            }
            if (sepIndex == -1) { return; }

            var split = strIndex.Split(Comparator);

            if (strIndex.Contains(".Invoke:"))
            {
                string[] invokeSplit = strIndex.Split(".Invoke:");
                Path = invokeSplit[0].Trim();
                ReplacerTemplate = strIndex.Substring(0, strIndex.IndexOf(".Invoke:") + ".Invoke:".Length);
                MatchCondition = "." + invokeSplit[1].Trim();
                SpecialHandling = SpecialHandlingType.Invoke;
            }
            else
            {
                Path = strIndex.Substring(0, sepIndex).Trim();
                ReplacerTemplate = strIndex.Substring(0, sepIndex); // unlike Path, can include whitespace provided by the user and also includes the separator comma
                MatchCondition = Comparator + " " + split[split.Length - 1].Trim();
            }

            Path = Path.Replace("{LAMBDA}", "=>");
            ReplacerTemplate = ReplacerTemplate.Replace("{LAMBDA}", "=>");
            MatchCondition = MatchCondition.Replace("{LAMBDA}", "=>");

            if (Path.StartsWith('!'))
            {
                Path = Path.Remove(0, 1).Trim();
            }
            parsed = true;
        }
        /// <summary>Creates an empty condition for callers that populate the fields directly (e.g. special-command conditions).</summary>
        private ArrayPathCondition()
        {

        }
        /// <summary>The subject path whose value is compared (the left side of the condition).</summary>
        public string Path;
        /// <summary>The exact substring (including user whitespace/operators) replaced by the parameter placeholder during expression formatting.</summary>
        public string ReplacerTemplate;
        /// <summary>The comparison/match portion appended after the substituted parameter.</summary>
        public string MatchCondition;
        /// <summary>Which custom operator (if any) this condition uses.</summary>
        public SpecialHandlingType SpecialHandling = SpecialHandlingType.None;
        /// <summary>The comparison operator (==, !=, &lt;, &gt;, ...) found in the condition.</summary>
        public string Comparator;

        /// <summary>Identifies which custom condition operator an <see cref="ArrayPathCondition"/> uses.</summary>
        public enum SpecialHandlingType
        {
            /// <summary>A plain comparison with no special operator.</summary>
            None,
            /// <summary>The <c>PatchableRaces</c> operator (membership in the patchable-race set).</summary>
            PatchableRaces,
            /// <summary>A boolean method invocation (e.g. <c>HasFlag(...)</c>) treated as <c>== true</c>.</summary>
            Invoke,
            /// <summary>The custom <c>MatchRace(...)</c> operator.</summary>
            MatchRace
        }

        /// <summary>Rewrites bare boolean <c>Invoke:</c> calls into explicit <c>== true</c> comparisons so they parse as conditions.</summary>
        /// <param name="argStr">The condition text possibly containing <c>Invoke:</c> calls.</param>
        /// <param name="replacedStr">Receives the rewritten text.</param>
        /// <returns><c>true</c> on success; <c>false</c> if an <c>Invoke:</c> call's arguments could not be parsed.</returns>
        private static bool ReplaceUncomparedInvokeCalls(string argStr, out string replacedStr) // replaces Invoke calls, which are assumed to be boolean, with a corresponding comparison (== true)
        {
            replacedStr = "";
            while (argStr.IndexOf("Invoke:") >= 0)
            {
                string[] split = argStr.Split("Invoke:");
                if (split.Length < 2) { return false; }
                if (GetFunctionArgsString(split[1], out string paramStr))
                {
                    argStr = split[0] + "invoke:" + paramStr + " == true";
                    if (split.Length > 2)
                    {
                        List<string> additionalText = new List<string>();
                        for (int i = 2; i < split.Length; i++)
                        {
                            additionalText.Add(split[i]);
                        }
                        argStr += string.Join("Invoke:", additionalText);
                    }
                }
            }

            replacedStr = argStr.Replace("invoke:", "Invoke:");
            return true;
        }

        /// <summary>Strips fully-enclosing paired parentheses from a string, repeatedly.</summary>
        /// <param name="str">The string to unwrap.</param>
        /// <returns>The string without redundant outer parentheses.</returns>
        /// <remarks>Naive: it only checks the first/last characters, so non-enclosing parens such as <c>(a) &amp;&amp; (b)</c> can be mis-stripped — see review notes.</remarks>
        private static string RemovePairedParens(string str)
        {
            while (str.StartsWith('(') && str.EndsWith(')'))
            {
                str = str.Substring(1, str.Length - 2);
            }
            return str;
        }

        /// <summary>Removes a single unmatched leading or trailing parenthesis, using quote- and depth-aware scanning so genuinely paired parentheses are preserved.</summary>
        /// <param name="str">The string to trim.</param>
        /// <returns>The string with a stray outer parenthesis removed if present.</returns>
        private static string TrimParens(string str)
        {
            if (str.StartsWith('('))
            {
                bool capture = true;
                int depth = 0;
                foreach (char c in str)
                {
                    if (capture && c == '"') { capture = false; }
                    else if (!capture && c == '"') { capture = true; }
                    else if (capture && c == '(') { depth++; }
                    else if (capture && c == ')') { depth--; }
                }

                if (depth > 0) { str = str.Remove(0, 1); }
            }

            if (str.EndsWith(')'))
            {
                bool capture = true;
                int depth = 0;
                foreach (char c in str)
                {
                    if (capture && c == '"') { capture = false; }
                    else if (!capture && c == '"') { capture = true; }
                    else if (capture && c == '(') { depth++; }
                    else if (capture && c == ')') { depth--; }
                }

                if (depth < 0) { str = str.Remove(str.Length - 1, 1); }
            }

            return str;
        }

        /// <summary>Extracts the parenthesized argument list (including nested parentheses) from the start of a function-call substring.</summary>
        /// <param name="subStr">Text beginning at or before the opening parenthesis.</param>
        /// <param name="parsedStr">Receives the captured text through the matching close parenthesis.</param>
        /// <returns><c>true</c> if a balanced parenthesis group was captured.</returns>
        private static bool GetFunctionArgsString(string subStr, out string parsedStr)
        {
            parsedStr = "";
            int bracketCount = 0;
            bool bracketsOpened = false;
            for (int i = 0; i < subStr.Length; i++)
            {
                char current = subStr[i];
                if (current == '(') { bracketCount++; bracketsOpened = true; }
                else if (current == ')') { bracketCount--; }

                parsedStr += current;
                if (bracketsOpened && bracketCount == 0)
                {
                    break;
                }
            }

            if (bracketsOpened && bracketCount == 0) { return true; }
            else { return false; }
        }

        /// <summary>The comparison operators recognized in array conditions.</summary>
        public static HashSet<string> Comparators = new HashSet<string>() { "==", "!=", "<", ">", "<=", ">=" };

        /// <summary>Splits a composite array condition on logical operators and parses each term into an <see cref="ArrayPathCondition"/>, handling the special <c>PatchableRaces</c> and <c>MatchRace</c> commands.</summary>
        /// <param name="input">The full bracketed condition expression.</param>
        /// <param name="parsed">Receives whether every term parsed successfully.</param>
        /// <returns>The parsed conditions (empty when parsing fails).</returns>
        public static List<ArrayPathCondition> GetConditionsFromString(string input, out bool parsed)
        {
            parsed = true;
            String[] result = input.Split(new Char[] { '|', '&' }, StringSplitOptions.RemoveEmptyEntries); // split on logical operators
            List<ArrayPathCondition> output = new List<ArrayPathCondition>();
            foreach (var conditionStr in result)
            {
                if (conditionStr.Contains("PatchableRaces")) // special command
                {
                    var patchableRaceArgs = conditionStr.Split("(");
                    var patchableRaceSubject = patchableRaceArgs[1].Trim();
                    var patchableRaceMethod = patchableRaceArgs[0].Replace("PatchableRaces.", "");

                    var patchableRaceCondition = new ArrayPathCondition { Path = patchableRaceSubject.Substring(0, patchableRaceSubject.Length - 1), MatchCondition = patchableRaceMethod.Trim(), SpecialHandling = SpecialHandlingType.PatchableRaces };
                    patchableRaceCondition.ReplacerTemplate = patchableRaceCondition.Path;
                    output.Add(patchableRaceCondition);
                    continue;
                }
                else if (conditionStr.Contains("MatchRace("))
                {
                    if (GetMatchRaceArgStr(conditionStr, out string args))
                    {
                        var matchRaceCondition = new ArrayPathCondition { Path = conditionStr, MatchCondition = args, SpecialHandling = SpecialHandlingType.MatchRace, ReplacerTemplate = conditionStr };
                        output.Add(matchRaceCondition);
                        continue;
                    }
                    else
                    {
                        parsed = false;
                        return output;
                    }
                }
                var condition = new ArrayPathCondition(conditionStr, out bool conditionParsed);
                if (conditionParsed) { output.Add(condition); }
                else
                {
                    parsed = false;
                    return new List<ArrayPathCondition>();
                }
            }
            return output;
        }

        /// <summary>Extracts the argument string inside the outermost parentheses of a <c>MatchRace(...)</c> condition.</summary>
        /// <param name="conditionStr">The condition text containing the call.</param>
        /// <param name="args">Receives the argument text.</param>
        /// <returns><c>true</c> if a parenthesized argument list was found.</returns>
        private static bool GetMatchRaceArgStr(string conditionStr, out string args)
        {
            args = "";
            int start = conditionStr.IndexOf('(');
            int end = conditionStr.LastIndexOf(')');
            if (start < 0 || end < 0)
            {
                return false;
            }

            args = conditionStr.Substring(start + 1, end - start - 1);
            return true;
        }
    }

    /// <summary>Substitutes each parsed condition's replacer template in the raw condition string with a positional parameter placeholder (<c>_0</c>, <c>_1</c>, ...) so the result can be compiled by DynamicExpresso.</summary>
    /// <param name="matchConditionStr">The raw condition string.</param>
    /// <param name="arrayMatchConditions">The parsed conditions, in order.</param>
    /// <returns>The condition string with subjects replaced by parameter placeholders.</returns>
    private static string FormatMatchConditionString(string matchConditionStr, List<ArrayPathCondition> arrayMatchConditions)
    {
        int argIndex = 0;
        char? previousChar = null;
        HashSet<char> variablePredecessors = new HashSet<char>() { '&', '|', '(' };

        foreach (var condition in arrayMatchConditions)
        {
            string argStr = "_" + argIndex;

            for (int i = 0; i < matchConditionStr.Length - condition.ReplacerTemplate.Length; i++)
            {
                var currentSubStr = matchConditionStr.Substring(i, condition.ReplacerTemplate.Length);
                if (i > 0) { previousChar = matchConditionStr[i - 1]; }
                else { previousChar = null; }

                if (currentSubStr == condition.ReplacerTemplate && (i == 0 || variablePredecessors.Contains(previousChar.Value)))
                {
                    matchConditionStr = matchConditionStr.Remove(i, condition.ReplacerTemplate.Length);
                    if (condition.SpecialHandling == ArrayPathCondition.SpecialHandlingType.Invoke)
                    {
                        matchConditionStr = matchConditionStr.Insert(i, argStr + ".");
                    }
                    else
                    {
                        matchConditionStr = matchConditionStr.Insert(i, argStr);
                    }
                    break;
                }
            }
            argIndex++;
        }
        return matchConditionStr;
    }

    /// <summary>Evaluates the custom <c>MatchRace</c> operator: resolves each comma-separated race path and returns whether any resolves to the NPC's race (or, with <c>MatchDefault</c>, the default race).</summary>
    /// <param name="rootRecord">The record the race paths are resolved against.</param>
    /// <param name="npcDyn">The NPC whose race is being matched (must be an <see cref="INpcGetter"/>).</param>
    /// <param name="toMatchPathStr">Comma-separated race paths, optionally including the literal <c>MatchDefault</c>.</param>
    /// <returns><c>true</c> if any candidate race matches the NPC's race.</returns>
    private bool MatchRace(dynamic rootRecord, dynamic npcDyn, string toMatchPathStr)
    {
        var npc = npcDyn as INpcGetter;
        if (npc == null) { return false; }

        var toMatch = toMatchPathStr.Split(',').Select(x => x.Trim()).ToHashSet();

        bool matchDefault = false;
        var defaultArg = toMatch.Where(x => x.Equals("MatchDefault", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (defaultArg is not null)
        {
            matchDefault = true;
            toMatch.Remove(defaultArg);
        }

        Dictionary<string, dynamic> subObjectCache = new Dictionary<string, dynamic>();

        foreach (var matchPath in toMatch)
        {
            if (GetObjectAtPath(rootRecord, npc, matchPath, subObjectCache, _environmentProvider.LinkCache, true, "", out dynamic outputObj))
            {
                var objCollection = outputObj as System.Collections.IEnumerable;

                if (objCollection is not null)
                {
                    foreach (var candidateRaceDyn in objCollection)
                    {
                        var candidateRaceForm = candidateRaceDyn as IFormLinkIdentifier;
                        if (candidateRaceForm is not null && (candidateRaceForm.FormKey.Equals(npc.Race.FormKey) || (matchDefault && candidateRaceForm.FormKey.Equals(Skyrim.Race.DefaultRace.FormKey))))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    var candidateRaceForm = outputObj as IFormLinkIdentifier;
                    if (candidateRaceForm is not null && (candidateRaceForm.FormKey.Equals(npc.Race.FormKey) || (matchDefault && candidateRaceForm.FormKey.Equals(Skyrim.Race.DefaultRace.FormKey))))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Returns the first array element satisfying a (possibly compound) boolean condition, evaluating the condition per element via DynamicExpresso and the custom race operators.</summary>
    /// <param name="variants">The candidate array elements.</param>
    /// <param name="matchConditionStr">The bracketed condition expression.</param>
    /// <param name="rootRecord">Root record for race/condition evaluation.</param>
    /// <param name="linkCache">Link cache for resolving records.</param>
    /// <param name="suppressMissingPathErrors">When true, suppresses missing-path error logging.</param>
    /// <param name="errorCaption">Caption for logged errors.</param>
    /// <param name="outputObj">Receives the first matching element, or null.</param>
    /// <param name="indexInParent">Receives the matching element's index.</param>
    /// <returns><c>true</c> if an element matched.</returns>
    private bool ChooseWhichArrayObject(IReadOnlyList<dynamic> variants, string matchConditionStr, IMajorRecordGetter rootRecord, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, out dynamic outputObj, out int? indexInParent)
    {
        outputObj = null;
        indexInParent = null;

        var arrayMatchConditions = ArrayPathCondition.GetConditionsFromString(matchConditionStr, out bool parsed);
        if (!parsed)
        {
            return false;
        }

        matchConditionStr = FormatMatchConditionString(matchConditionStr, arrayMatchConditions);

        //catch for user type Invoke: but hasn't yet finished the function
        if (matchConditionStr.Contains(".Invoke:")) { return false; }

        int patchableRaceArgIndex = arrayMatchConditions.Count;
        bool addPatchableRaceArg = false;

        for (int i = 0; i < variants.Count(); i++)
        {
            var candidateObj = variants[i];
            List<dynamic> evalParameters = new List<dynamic>();
            int argIndex = 0;
            bool skipToNext = false;
            Dictionary<string, dynamic> variantObjectCache = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            IMajorRecordGetter candidateRecordGetter = null;

            bool candidateObjIsRecord = ObjectHasFormKey(candidateObj, out FormKey? objFormKey) && objFormKey != null;
            bool candidateObjIsResolved = objFormKey != null && !objFormKey.Value.IsNull && linkCache.TryResolve(objFormKey.Value, (Type)candidateObj.Type, out candidateRecordGetter);

            foreach (var condition in arrayMatchConditions)
            {
                if (condition.SpecialHandling == ArrayPathCondition.SpecialHandlingType.MatchRace)
                {
                    if (MatchRace(candidateRecordGetter, rootRecord, condition.MatchCondition))
                    {
                        matchConditionStr = matchConditionStr.Replace(condition.ReplacerTemplate, " true");
                        continue;
                    }
                    else
                    {
                        skipToNext = true;
                        break;
                    }
                }

                dynamic comparisonObject;

                if (candidateObjIsResolved && candidateRecordGetter != null && GetObjectAtPath(candidateRecordGetter, rootRecord, condition.Path, variantObjectCache, linkCache, suppressMissingPathErrors, errorCaption, out comparisonObject))
                {
                    evalParameters.Add(comparisonObject);
                }
                else if (candidateObjIsRecord) // Can occur if link cache is record template link cache and the given variant belongs to a non-template plugin (ex if variants is an armature array and the current variant is an armature from the base game)
                {
                    skipToNext = true;
                    break;
                }
                else if (GetObjectAtPath(candidateObj, rootRecord, condition.Path, variantObjectCache, linkCache, suppressMissingPathErrors, errorCaption, out comparisonObject))
                {
                    evalParameters.Add(comparisonObject);
                }
                else
                {
                    return false;
                }
                argIndex++;

                if (condition.SpecialHandling == ArrayPathCondition.SpecialHandlingType.PatchableRaces)
                {
                    matchConditionStr = matchConditionStr.Replace("PatchableRaces", "_" + patchableRaceArgIndex);
                    addPatchableRaceArg = true;
                    var raceGetter = (IFormKeyGetter)evalParameters[evalParameters.Count - 1];
                    evalParameters[evalParameters.Count - 1] = raceGetter.FormKey.ToLinkGetter<IRaceGetter>();
                }
            }
            if (skipToNext) { continue; }

            // reference PatchableRaces if necessary
            if (addPatchableRaceArg)
            {
                evalParameters.Add(_raceResolver.PatchableRaces);
            }

            try
            {
                if (EvalBoolExpression(matchConditionStr, evalParameters))
                {
                    outputObj = candidateObj;
                    indexInParent = i;
                    return true;
                }
            }
            catch
            {
                return false; // should only happen when user is screwing around with UI
            }
        }
        return false;
    }

    /// <summary>Collects every array element satisfying a (possibly compound) boolean condition (the multi-match counterpart of <see cref="ChooseWhichArrayObject"/>).</summary>
    /// <param name="variants">The candidate array elements.</param>
    /// <param name="rootRecord">Root record for race/condition evaluation.</param>
    /// <param name="matchConditionStr">The bracketed condition expression.</param>
    /// <param name="linkCache">Link cache for resolving records.</param>
    /// <param name="suppressMissingPathErrors">When true, suppresses missing-path error logging.</param>
    /// <param name="errorCaption">Caption for logged errors.</param>
    /// <param name="matchedObjects">Receives all matching elements.</param>
    /// <returns><c>true</c> if at least one element matched.</returns>
    private bool ChooseSelectedArrayObjects(IReadOnlyList<dynamic> variants, IMajorRecordGetter rootRecord, string matchConditionStr, ILinkCache linkCache, bool suppressMissingPathErrors, string errorCaption, List<dynamic> matchedObjects)
    {
        var arrayMatchConditions = ArrayPathCondition.GetConditionsFromString(matchConditionStr, out bool parsed);
        if (!parsed)
        {
            return false;
        }

        matchConditionStr = FormatMatchConditionString(matchConditionStr, arrayMatchConditions);

        int patchableRaceArgIndex = arrayMatchConditions.Count;
        bool addPatchableRaceArg = false;

        for (int i = 0; i < variants.Count(); i++)
        {
            var candidateObj = variants[i];
            List<dynamic> evalParameters = new List<dynamic>();
            int argIndex = 0;
            bool skipToNext = false;
            Dictionary<string, dynamic> variantObjectCache = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            IMajorRecordGetter candidateRecordGetter = null;

            bool candidateObjIsRecord = ObjectHasFormKey(candidateObj, out FormKey? objFormKey) && objFormKey != null;
            bool candidateObjIsResolved = objFormKey != null && !objFormKey.Value.IsNull && linkCache.TryResolve(objFormKey.Value, (Type)candidateObj.Type, out candidateRecordGetter);

            foreach (var condition in arrayMatchConditions)
            {
                if (condition.SpecialHandling == ArrayPathCondition.SpecialHandlingType.MatchRace)
                {
                    if (MatchRace(candidateRecordGetter, rootRecord, condition.MatchCondition))
                    {
                        matchConditionStr = matchConditionStr.Replace(condition.ReplacerTemplate, " true");
                        continue;
                    }
                    else
                    {
                        skipToNext = true;
                        break;
                    }
                }

                dynamic comparisonObject;

                if (candidateObjIsResolved && candidateRecordGetter != null && GetObjectAtPath(candidateRecordGetter, rootRecord, condition.Path, variantObjectCache, linkCache, suppressMissingPathErrors, errorCaption, out comparisonObject))
                {
                    evalParameters.Add(comparisonObject);
                }
                else if (candidateObjIsRecord) // warn if the object is a record but the corresponding Form couldn't be resolved
                {
                    _logger.LogError("Could not resolve record for array member object " + objFormKey.Value.ToString());
                    skipToNext = true;
                    break;
                }
                else if (GetObjectAtPath(candidateObj, rootRecord, condition.Path, variantObjectCache, linkCache, suppressMissingPathErrors, errorCaption, out comparisonObject))
                {
                    evalParameters.Add(comparisonObject);
                }
                else
                {
                    return false;
                }
                argIndex++;

                if (condition.SpecialHandling == ArrayPathCondition.SpecialHandlingType.PatchableRaces)
                {
                    matchConditionStr = matchConditionStr.Replace("PatchableRaces", "_" + patchableRaceArgIndex);
                    addPatchableRaceArg = true;
                    var raceGetter = (IFormKeyGetter)evalParameters[evalParameters.Count - 1];
                    evalParameters[evalParameters.Count - 1] = raceGetter.FormKey.ToLinkGetter<IRaceGetter>();
                }
            }
            if (skipToNext) { continue; }

            // reference PatchableRaces if necessary
            if (addPatchableRaceArg)
            {
                evalParameters.Add(_raceResolver.PatchableRaces);
            }

            try
            {
                if (EvalBoolExpression(matchConditionStr, evalParameters))
                {
                    matchedObjects.Add(candidateObj);
                }
            }
            catch
            {
                return false; // should only happen when user is screwing around with UI
            }
        }
        return matchedObjects.Any();
    }

    /// <summary>Determines whether a path segment is an array specifier of the form <c>[index]</c> and extracts the inner specifier.</summary>
    /// <param name="path">The path segment to test.</param>
    /// <param name="index">Receives the text between the brackets.</param>
    /// <returns><c>true</c> if the segment is bracketed.</returns>
    private static bool PathIsArray(string path, out string index) //correct input is of form [y]
    {
        index = "";
        if (path.StartsWith('[') && path.EndsWith(']'))
        {
            index = path.Substring(1, path.IndexOf(']') - 1);
            return true;
        }
        return false;
    }

    /// <summary>Determines whether a path segment is an array specifier of the form <c>[index]</c>.</summary>
    /// <param name="path">The path segment to test.</param>
    /// <returns><c>true</c> if the segment is bracketed.</returns>
    public static bool PathIsArray(string path) //correct input is of form [y]
    {
        return PathIsArray(path, out string _);
    }

    /// <summary>Determines the Loqui/record type to use when resolving an object — its own type if it's a Loqui type, or the target type of a (nullable) form link.</summary>
    /// <param name="currentObject">The object to inspect.</param>
    /// <param name="registerType">Receives the resolved type, or null.</param>
    /// <returns><c>true</c> if a usable type was found.</returns>
    public static bool TryGetRegister(dynamic currentObject, out Type registerType)
    {
        Type objType = currentObject.GetType();
        if (LoquiRegistration.IsLoquiType(objType))
        {
            registerType = objType;
            return true;
        }
        else if (objType.Name == "FormLink`1")
        {
            var formLink = currentObject as IFormLinkGetter;
            if (formLink != null)
            {
                registerType = formLink.Type;
                return true;
            }
        }
        else if (objType.Name == "FormLinkNullable`1" && currentObject.Type != null)
        {
            registerType = currentObject.Type;
            return true;
        }
        registerType = null;
        return false;
    }

    /// <summary>Reads a named property from an object via reflection.</summary>
    /// <param name="root">The object to read from.</param>
    /// <param name="propertyName">The property to read.</param>
    /// <param name="outputObj">Receives the property value, or null.</param>
    /// <returns><c>true</c> if the property exists, is a simple (parameterless) getter, and returned a non-null value.</returns>
    public static bool GetSubObject(dynamic root, string propertyName, out dynamic outputObj)
    {
        Type type = root.GetType();
        var prop = type.GetProperty(propertyName);
        if (prop is not null && prop.GetMethod?.GetParameters().Length == 0) // length check because some weird getters have multiple parameters and I'm not sure how to deal with them yet.
        {
            outputObj = prop.GetValue(root);
            if (outputObj is not null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            outputObj = null;
            return false;
        }

        // The following code was meant to speed up the patcher by cacheing get methods, but I could never figure out how to do it for generic types.
        /*
        outputObj = null;
        if (GetAccessor(root, propertyName, AccessorType.Getter, out Delegate getter))
        {
            outputObj = getter.DynamicInvoke(root);
            return true;
        }
        else
        {
            return false;
        }
        */
    }

    /// <summary>Sets a named property on an object via reflection, logging a detailed error on failure.</summary>
    /// <param name="root">The object to mutate.</param>
    /// <param name="propertyName">The property to set.</param>
    /// <param name="value">The value to assign.</param>
    /// <returns><c>true</c> if the property existed and was set without throwing.</returns>
    public bool SetPropertyValue(dynamic root, string propertyName, dynamic value)
    {
        Type type = root.GetType();
        var prop = type.GetProperty(propertyName);
        try
        {
            if (prop != null)
            {
                prop.SetValue(root, value);
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception e)
        {
            Type valueType = value.GetType();
            string exceptionStr = "Error setting object property:" + Environment.NewLine +
                "Object type: " + type + Environment.NewLine +
                "Property name: " + propertyName + Environment.NewLine +
                "Value type " + valueType + Environment.NewLine;
            if (valueType == typeof(int) || valueType == typeof(float) || valueType == typeof(string))
            {
                exceptionStr += "Value: " + value + Environment.NewLine;
            }
            exceptionStr += "Exception:" + Environment.NewLine + ExceptionLogger.GetExceptionStack(e);
            _logger.LogError(exceptionStr);
            return false;
        }

        // The following code was meant to speed up the patcher by cacheing set methods, but I could never figure out how to do it for generic types.
        /* 
        if (GetAccessor(root, propertyName, AccessorType.Setter, out Delegate setter))
        {
            setter.DynamicInvoke(root, value);
            return true;
        }
        else
        {
            return false;
        }
        */
    }

    /// <summary>Determines whether an object exposes a <c>FormKey</c> property and returns it.</summary>
    /// <param name="obj">The object to inspect.</param>
    /// <param name="formKey">Receives the FormKey, or null when absent.</param>
    /// <returns><c>true</c> if the object has a FormKey.</returns>
    public static bool ObjectHasFormKey(dynamic obj, out FormKey? formKey)
    {
        bool hasFormKey = GetSubObject(obj, "FormKey", out dynamic formKeyDyn);
        if (hasFormKey)
        {
            formKey = (FormKey)formKeyDyn;
            return true;
        }
        else
        {
            formKey = null;
            return false;
        }
    }

    /// <summary>Determines whether an object exposes a <c>FormKey</c> property.</summary>
    /// <param name="obj">The object to inspect.</param>
    /// <returns><c>true</c> if the object has a FormKey.</returns>
    public static bool ObjectHasFormKey(dynamic obj)
    {
        return GetSubObject(obj, "FormKey", out dynamic _);
    }

    /// <summary>Splits a record path into segments on dots — but not dots inside bracketed array specifiers — and separates a trailing <c>[..]</c> specifier into its own segment.</summary>
    /// <param name="input">The full record path.</param>
    /// <returns>The ordered path segments.</returns>
    public static string[] SplitPath(string input)
    {
        var pattern = @"\.(?![^\[]*[\]])";
        var split = Regex.Split(input, pattern);

        List<string> output = new List<string>();
        foreach (var substr in split)
        {
            if (!substr.StartsWith('[') && substr.Contains('[') && substr.EndsWith(']'))
            {
                int bracketIndex = substr.IndexOf('[');
                output.Add(substr.Substring(0, bracketIndex));
                output.Add(substr.Substring(bracketIndex, substr.Length - bracketIndex));
            }
            else
            {
                output.Add(substr);
            }
        }

        return output.ToArray();
    }

    /// <summary>Per-type cache of resolved <see cref="PropertyInfo"/> by property name, used by <see cref="GetPropertyInfo"/>.</summary>
    public static Dictionary<Type, Dictionary<string, System.Reflection.PropertyInfo>> PropertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();

    /// <summary>Resolves a property's <see cref="PropertyInfo"/> by reflection without caching. Retained for performance comparison.</summary>
    /// <param name="obj">The object whose type is inspected.</param>
    /// <param name="propertyName">The property to resolve.</param>
    /// <param name="property">Receives the resolved <see cref="PropertyInfo"/>, or null.</param>
    /// <returns><c>true</c> if the property exists.</returns>
    public static bool GetPropertyInfo_NoCache(dynamic obj, string propertyName, out System.Reflection.PropertyInfo property) // for performance testing only
    {
        property = null;
        Type type = obj.GetType();
        property = type.GetProperty(propertyName);
        if (property != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    /// <summary>Resolves a property's <see cref="PropertyInfo"/> by reflection, caching the result per type in <see cref="PropertyCache"/>.</summary>
    /// <param name="obj">The object whose type is inspected.</param>
    /// <param name="propertyName">The property to resolve.</param>
    /// <param name="property">Receives the resolved <see cref="PropertyInfo"/>, or null.</param>
    /// <returns><c>true</c> if the property exists.</returns>
    /// <remarks>The cache is a non-thread-safe dictionary; see review notes.</remarks>
    public static bool GetPropertyInfo(dynamic obj, string propertyName, out System.Reflection.PropertyInfo property)
    {
        property = null;
        Type type = obj.GetType();
        if (PropertyCache.ContainsKey(type))
        {
            var properties = PropertyCache[type];
            if (properties.ContainsKey(propertyName))
            {
                property = properties[propertyName];
            }
            else
            {
                property = type.GetProperty(propertyName);
                PropertyCache[type].Add(propertyName, property);
            }
        }
        else
        {
            Dictionary<string, System.Reflection.PropertyInfo> subDict = new Dictionary<string, PropertyInfo>();
            property = type.GetProperty(propertyName);
            subDict.Add(propertyName, property);
            PropertyCache.Add(type, subDict);
        }

        if (property != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>Per-type cache of compiled getter delegates by property name (used by <see cref="GetAccessor"/>).</summary>
    public static Dictionary<Type, Dictionary<string, Delegate>> GetterEmbassy = new Dictionary<Type, Dictionary<string, Delegate>>();
    /// <summary>Per-type cache of compiled setter delegates by property name (used by <see cref="GetAccessor"/>).</summary>
    public static Dictionary<Type, Dictionary<string, Delegate>> SetterEmbassy = new Dictionary<Type, Dictionary<string, Delegate>>();

    /// <summary>Selects whether an accessor delegate is a property getter or setter.</summary>
    public enum AccessorType
    {
        /// <summary>A property getter.</summary>
        Getter,
        /// <summary>A property setter.</summary>
        Setter
    }

    /// <summary>Builds a getter/setter delegate for a property without caching. Retained for performance comparison.</summary>
    /// <param name="obj">The object whose type is inspected.</param>
    /// <param name="propertyName">The property to build an accessor for.</param>
    /// <param name="accessorType">Whether to build a getter or setter.</param>
    /// <param name="accessor">Receives the delegate, or null.</param>
    /// <returns><c>true</c> if an accessor was built.</returns>
    public static bool GetAccessor_NoCache(dynamic obj, string propertyName, AccessorType accessorType, out Delegate accessor) // for performance testing only
    {
        accessor = null;
        PropertyInfo property = null;

        if (GetPropertyInfo(obj, propertyName, out property))
        {
            switch (accessorType)
            {
                case AccessorType.Getter: accessor = CreateDelegateGetter(property); break;
                case AccessorType.Setter: accessor = CreateDelegateSetter(property); break;
            }
        }
        else
        {
            accessor = null; // just for readability
        }

        if (accessor != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>Builds (and caches per type) a getter or setter delegate for a property.</summary>
    /// <param name="obj">The object whose type is inspected.</param>
    /// <param name="propertyName">The property to build an accessor for.</param>
    /// <param name="accessorType">Whether to build a getter or setter.</param>
    /// <param name="accessor">Receives the cached/created delegate, or null.</param>
    /// <returns><c>true</c> if an accessor was built.</returns>
    /// <remarks>Part of the disabled delegate-caching experiment (see the commented-out blocks in <see cref="GetSubObject"/>/<see cref="SetPropertyValue"/>); caches are non-thread-safe — see review notes.</remarks>
    public static bool GetAccessor(dynamic obj, string propertyName, AccessorType accessorType, out Delegate accessor)
    {
        accessor = null;
        PropertyInfo property = null;
        Dictionary<Type, Dictionary<string, Delegate>> cache = null;

        if (obj == null) { return false; }

        switch (accessorType)
        {
            case AccessorType.Getter: cache = GetterEmbassy; break;
            case AccessorType.Setter: cache = SetterEmbassy; break;
        }

        Type type = obj.GetType();

        if (cache.ContainsKey(type))
        {
            if (cache[type].ContainsKey(propertyName))
            {
                accessor = cache[type][propertyName];
            }
            else if (GetPropertyInfo(obj, propertyName, out property))
            {
                switch (accessorType)
                {
                    case AccessorType.Getter: accessor = CreateDelegateGetter(property); break;
                    case AccessorType.Setter: accessor = CreateDelegateSetter(property); break;
                }

                cache[type].Add(propertyName, accessor);
            }
        }
        else
        {
            if (GetPropertyInfo(obj, propertyName, out property))
            {
                switch (accessorType)
                {
                    case AccessorType.Getter: accessor = CreateDelegateGetter(property); break;
                    case AccessorType.Setter: accessor = CreateDelegateSetter(property); break;
                }
                Dictionary<string, Delegate> subDict = new Dictionary<string, Delegate>();
                subDict.Add(propertyName, accessor);
                cache.Add(type, subDict);
            }
            else
            {
                accessor = null; // just for readability
            }
        }

        if (accessor != null)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>Creates a strongly-typed getter delegate from a property's get method.</summary>
    /// <param name="property">The property to wrap.</param>
    /// <returns>A delegate that reads the property given an instance.</returns>
    public static Delegate CreateDelegateGetter(PropertyInfo property)
    {
        var delegateType = Expression.GetFuncType(property.DeclaringType, property.GetMethod.ReturnType);
        return property.GetMethod.CreateDelegate(delegateType);
    }

    /// <summary>Creates a setter delegate from a property's set method.</summary>
    /// <param name="property">The property to wrap.</param>
    /// <returns>A delegate that writes the property.</returns>
    public static Delegate CreateDelegateSetter(PropertyInfo property)
    {
        var delegateType = Expression.GetActionType(property.PropertyType);
        return Delegate.CreateDelegate(delegateType, null, property.GetSetMethod());
    }
}