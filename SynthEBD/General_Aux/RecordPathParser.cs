using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.RegularExpressions;
using Z.Expressions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using FastMember;
namespace SynthEBD
{
    public class RecordPathParser
    {
        public static dynamic GetObjectAtPath(dynamic rootObj, string relativePath, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (rootObj == null)
            {
                return null;
            }

            Dictionary<string, dynamic> objectCache;

            if (objectLinkMap.ContainsKey(rootObj))
            {
                objectCache = objectLinkMap[rootObj];
            }
            else
            {
                objectCache = new Dictionary<string, dynamic>();
                objectLinkMap.Add(rootObj, objectCache);
            }

            string[] splitPath = SplitPath(relativePath);
            dynamic currentObj = rootObj;

            for (int i = 0; i < splitPath.Length; i++)
            {
                if (currentObj == null)
                {
                    return null;
                }

                // check object cache to see if the given object has already been resolved
                string concatPath = String.Join(".", splitPath.ToList().GetRange(0, i+1));
                if (objectCache.ContainsKey(concatPath))
                {
                    currentObj = objectCache[concatPath];
                    continue;
                }

                // otherwise search for the given value via Reflection
                string currentSubPath = splitPath[i];

                // handle arrays
                if (PathIsArray(currentSubPath, out string arraySubPath, out string arrIndex))
                {
                    currentObj = GetArrayObjectAtIndex(currentObj, arraySubPath, arrIndex, objectLinkMap, linkCache);
                }
                else
                {
                    currentObj = GetSubObject(currentObj, currentSubPath);
                }

                // if the current property is another record, resolve it to traverse
                if (ObjectIsRecord(currentObj, out FormKey? subrecordFK))
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

            return currentObj;
        }

        private static dynamic GetArrayObjectAtIndex(dynamic currentObj, string arraySubPath, string arrIndex, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var collectionObj = (IEnumerable<dynamic>)GetObjectAtPath(currentObj, arraySubPath, objectLinkMap, linkCache);
            if (collectionObj == null)
            {
                return null;
            }

            //if array index is numeric
            if (int.TryParse(arrIndex, out int iIndex))
            {
                if (iIndex < collectionObj.Count())
                {
                    currentObj = collectionObj.ElementAt(iIndex);
                }
                else
                {
                    string currentSubPath = arraySubPath + "[" + arrIndex + "]";
                    Logger.LogError("Could not get object at " + currentSubPath + " because " + arraySubPath + " does not have an element at index " + iIndex);
                    return null;
                }
            }

            // if array index specifies object by property, figure out which index is the right one
            else
            {
                currentObj = ChooseWhichArrayObject(collectionObj, arrIndex, objectLinkMap, linkCache);
                if (currentObj == null)
                {
                    string currentSubPath = arraySubPath + "[" + arrIndex + "]";
                    Logger.LogError("Could not get object at " + currentSubPath + " because " + arraySubPath + " does not have an element that matches condition: " + arrIndex);
                    return null;
                }
            }

            return currentObj;
        }

        private class ArrayPathCondition
        {
            private ArrayPathCondition(string strIndex)
            {
                int sepIndex = strIndex.LastIndexOf(',');
                Path = strIndex.Substring(0, sepIndex).Trim();
                ReplacerTemplate = strIndex.Substring(0, sepIndex) + ","; // unlike Path, can include whitespace provided by the user and also includes the separator comma
                MatchCondition = strIndex.Substring(sepIndex + 1, strIndex.Length - sepIndex - 1).Trim();
                if (Path.StartsWith('!'))
                {
                    Path = Path.Remove(0, 1).Trim();
                }
            }
            private ArrayPathCondition()
            {

            }
            public string Path;
            public string ReplacerTemplate;
            public string MatchCondition;
            public string SpecialHandling = "";

            public static List<ArrayPathCondition> GetConditionsFromString(string input)
            {
                String[] result = input.Split(new Char[] { '|', '&' }, StringSplitOptions.RemoveEmptyEntries); // split on logical operators
                List<ArrayPathCondition> output = new List<ArrayPathCondition>();
                foreach (var conditionStr in result)
                {
                    if (conditionStr.Contains("PatchableRaces")) // special command
                    {
                        var patchableRaceArgs = conditionStr.Split("(");
                        var patchableRaceSubject = patchableRaceArgs[1].Trim();
                        var patchableRaceMethod = patchableRaceArgs[0].Replace("PatchableRaces.", "");

                        var patchableRaceCondition = new ArrayPathCondition { Path = patchableRaceSubject.Substring(0, patchableRaceSubject.Length - 1), MatchCondition = patchableRaceMethod.Trim(), SpecialHandling = "PatchableRaces"};
                        patchableRaceCondition.ReplacerTemplate = patchableRaceCondition.Path;
                        output.Add(patchableRaceCondition);
                        continue;
                    }
                    output.Add(new ArrayPathCondition(conditionStr));
                }
                return output;
            }
        }

        private static dynamic ChooseWhichArrayObject(IEnumerable<dynamic> variants, string matchConditionStr, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var arrayMatchConditions = ArrayPathCondition.GetConditionsFromString(matchConditionStr);

            int argIndex = 0;
            foreach (var condition in arrayMatchConditions)
            {
                string argStr = '{' + argIndex.ToString() + '}';
                //try using RegEx or some other algorithm to replace whole word only. This is temp solution
                //matchConditionStr = matchConditionStr.Replace(condition.ReplacerTemplate, argStr);
                for (int i = 0; i < matchConditionStr.Length - condition.ReplacerTemplate.Length; i++)
                {
                    if (matchConditionStr.Substring(i, condition.ReplacerTemplate.Length) == condition.ReplacerTemplate && (i == 0 || matchConditionStr[i - 1] == '(' || matchConditionStr[i - 1] == ' '))
                    {
                       matchConditionStr = matchConditionStr.Remove(i, condition.ReplacerTemplate.Length);
                       matchConditionStr = matchConditionStr.Insert(i, argStr);
                    }
                }
                argIndex++;
            }

            int patchableRaceArgIndex = argIndex;
            bool addPatchableRaceArg = false;

            foreach (var candidateObj in variants)
            {
                List<dynamic> evalParameters = new List<dynamic>();
                argIndex = 0;
                bool skipToNext = false;

                IMajorRecordCommonGetter candidateRecordGetter = null;

                bool candidateObjIsRecord = ObjectIsRecord(candidateObj, out FormKey? objFormKey) && objFormKey != null;
                bool candidateObjIsResolved = !objFormKey.Value.IsNull && linkCache.TryResolve(objFormKey.Value, (Type)candidateObj.Type, out candidateRecordGetter);

                foreach (var condition in arrayMatchConditions)
                {
                    dynamic comparisonObject;
                    
                    if (candidateObjIsResolved && candidateRecordGetter != null)
                    {
                        comparisonObject = GetObjectAtPath(candidateRecordGetter, condition.Path, objectLinkMap, linkCache);
                        evalParameters.Add(candidateRecordGetter);
                    }
                    else if (candidateObjIsRecord) // warn if the object is a record but the corresponding Form couldn't be resolved
                    {
                        Logger.LogError("Could not resolve record for array member object " + objFormKey.Value.ToString());
                        skipToNext = true;
                        break;
                    }
                    else
                    {
                        comparisonObject = GetObjectAtPath(candidateObj, condition.Path, objectLinkMap, linkCache);
                        evalParameters.Add(comparisonObject);
                    }
                    argIndex++;

                    if (condition.SpecialHandling == "PatchableRaces")
                    {
                        matchConditionStr = matchConditionStr.Replace("PatchableRaces", '{' + patchableRaceArgIndex.ToString() + "}");
                        addPatchableRaceArg = true;
                        evalParameters[evalParameters.Count - 1] = evalParameters[evalParameters.Count - 1].FormKey.AsLinkGetter<IRaceGetter>();
                    }
                }
                if(skipToNext) { continue; }
                
                // reference PatchableRaces if necessary
                if (addPatchableRaceArg) 
                {
                    evalParameters.Add(MainLoop.PatchableRaces); 
                }

                if (Eval.Execute<bool>(matchConditionStr, evalParameters.ToArray()))
                {
                    return candidateObj;
                }
            }
            return null;
        }

        private static bool PathIsArray(string path, out string subPath, out string index) //correct input is of form x[y]
        {
            if (path.Contains('['))
            {
                var tmp = path.Split('[');

                subPath = tmp[0]; //x
                var tmp1 = tmp[1]; //y]

                if (tmp1.Contains(']'))
                {
                    index = tmp1.Split(']')[0]; //y
                    return true;
                }
            }

            subPath = "";
            index = "";
            return false;
        }

        public static dynamic GetSubObject(dynamic root, string propertyName)
        {
            //FastMember
            /*
            var accessor = TypeAccessor.Create(root.GetType());
            MemberSet members = accessor.GetMembers();
            if (members.Where(x => x.Name == propertyName).Any())
            {
                return accessor[root, propertyName];
            }
            else
            {
                return null;
            }
            */

            Type type = root.GetType();
            
            if (MainLoop.PropertyCache.ContainsKey(type))
            {
                var subDict = MainLoop.PropertyCache[type];
                if (subDict.ContainsKey(propertyName))
                {
                    var cachedProperty = subDict[propertyName];
                    if (cachedProperty != null)
                    {
                        return cachedProperty.GetValue(root);
                    }
                }
                else
                {
                    var newProperty = type.GetProperty(propertyName);
                    subDict.Add(propertyName, newProperty);
                    if (newProperty != null)
                    {
                        return newProperty.GetValue(root);
                    }
                }
            }
            else
            {
                var newSubDict = new Dictionary<string, PropertyInfo>();
                var newProperty2 = type.GetProperty(propertyName);
                newSubDict.Add(propertyName, newProperty2);
                MainLoop.PropertyCache.Add(type, newSubDict);
                if (newProperty2 != null)
                {
                    return newProperty2.GetValue(root);
                }
            }
            return null;
            /*
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                return property.GetValue(root);
            }
            else
            {
                return null;
            }
            */
        }

        public static void SetSubObject(dynamic root, string propertyName, dynamic value)
        {
            //FastMember
            /*
            var accessor = TypeAccessor.Create(root.GetType());
            MemberSet members = accessor.GetMembers();
            if (members.Where(x => x.Name == propertyName).Any())
            {
                accessor[root, propertyName] = value;
            }
            else
            {
                Logger.LogReport("Error: Could not set " + propertyName + " to " + value + " because the root object of type " + root.GetType() + " does not contain this property.");
            }
            */

            Type type = root.GetType();

            if (MainLoop.PropertyCache.ContainsKey(type))
            {
                var subDict = MainLoop.PropertyCache[type];
                if (subDict.ContainsKey(propertyName))
                {
                    var cachedProperty = subDict[propertyName];
                    if (cachedProperty != null)
                    {
                        cachedProperty.SetValue(root, value);
                    }
                }
                else
                {
                    var newProperty = type.GetProperty(propertyName);
                    subDict.Add(propertyName, newProperty);
                    if (newProperty != null)
                    {
                        newProperty.SetValue(root, value);
                    }
                }
            }
            else
            {
                var newSubDict = new Dictionary<string, PropertyInfo>();
                var newProperty2 = type.GetProperty(propertyName);
                newSubDict.Add(propertyName, newProperty2);
                MainLoop.PropertyCache.Add(type, newSubDict);
                if (newProperty2 != null)
                {
                    newProperty2.SetValue(root, value);
                }
            }

            /*
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                property.SetValue(root, value);
            }
            */
        }

        /// <summary>
        /// Determines if root[propertyName] expects a record (as opposed to a generic struct). Outs the FormKey of root[propertyName].value if one exists, or outs null if there is no value at that property
        /// </summary>
        /// <param name="root">Root object</param>
        /// <param name="propertyName">Property to search relative to root object</param>
        /// <param name="formKey">Nullable formkey of root[propertyName] if it exists</param>
        /// <returns></returns>
        public static bool PropertyIsRecord(dynamic root, string propertyName, out FormKey? formKey, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            // handle arrays
            if (PathIsArray(propertyName, out string arraySubPath, out string arrIndex))
            {
                var specifiedArrayObj = GetArrayObjectAtIndex(root, arraySubPath, arrIndex, objectLinkMap, linkCache);
                return ObjectIsRecord(specifiedArrayObj, out formKey);
            }
            else
            {
                formKey = null;
                var property = root.GetType().GetProperty(propertyName);
                if (property != null && property.PropertyType.Name.StartsWith("IFormLinkNullableGetter"))
                {
                    formKey = (FormKey)GetSubObject(property.GetValue(root), "FormKey");
                    return true;
                }
                return false;
            }
        }

        public static bool PropertyIsRecord(dynamic root, string propertyName, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            // handle arrays
            if (PathIsArray(propertyName, out string arraySubPath, out string arrIndex))
            {
                root = GetArrayObjectAtIndex(root, arraySubPath, arrIndex, objectLinkMap, linkCache);
            }

            var property = root.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType.Name.StartsWith("IFormLinkNullableGetter"))
            {
                return true;
            }
            return false;
        }

        public static bool ObjectIsRecord(dynamic obj, out FormKey? formKey)
        {
            if (obj != null)
            {
                var property = obj.GetType().GetProperty("FormKey");
                if (property != null)
                {
                    formKey = (FormKey)property.GetValue(obj);
                    return true;
                }
            }

            formKey = null;
            return false;
        }

        public static bool ObjectIsRecord(dynamic obj)
        {
            var property = obj.GetType().GetProperty("FormKey");
            if (property != null)
            {
                return true;
            }
            return false;
        }

        public static bool HasProperty(Type type, string propertyName)
        {
            return type.GetProperty(propertyName) != null;
        }

        public static string[] SplitPath(string input)
        {
            var pattern = @"\.(?![^\[]*[\]])";
            return Regex.Split(input, pattern);
        }

    }
}
