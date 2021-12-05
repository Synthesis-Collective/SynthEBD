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
using Loqui;

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
                //if (PropertyIsRecord(currentObj, currentSubPath, out FormKey? subrecordFK, objectLinkMap, linkCache))
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
                // arrIndex will look like "a.b.c.d,HasFlag(x)"

                int sepIndex = arrIndex.LastIndexOf(',');
                string subPath = arrIndex.Substring(0, sepIndex);
                string matchCondition = arrIndex.Substring(sepIndex + 1, arrIndex.Length - sepIndex - 1);
                currentObj = ChooseWhichArrayObject(collectionObj, subPath, matchCondition, objectLinkMap, linkCache);
                if (currentObj == null)
                {
                    string currentSubPath = arraySubPath + "[" + arrIndex + "]";
                    Logger.LogError("Could not get object at " + currentSubPath + " because " + arraySubPath + " does not have an element that matches condition: " + arrIndex);
                    return null;
                }
            }

            return currentObj;
        }

        private static dynamic ChooseWhichArrayObject(IEnumerable<dynamic> variants, string subPath, string matchCondition, Dictionary<dynamic, Dictionary<string, dynamic>> objectLinkMap, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            string expression = "{0}" + matchCondition;

            foreach (var candidateObj in variants)
            {
                dynamic comparisonObject;
                if (ObjectIsRecord(candidateObj, out FormKey? objFormKey) && objFormKey != null)
                {
                    if (!objFormKey.Value.IsNull && linkCache.TryResolve(objFormKey.Value, (Type)candidateObj.Type, out var candidateRecordGetter) && candidateRecordGetter != null)
                    {
                        comparisonObject = GetObjectAtPath(candidateRecordGetter, subPath, objectLinkMap, linkCache);
                        if (comparisonObject != null && Eval.Execute<bool>(expression, comparisonObject))
                        {
                            return candidateObj;
                        }
                    }
                    else if (!objFormKey.Value.IsNull) // warn if the object is a record but the corresponding Form couldn't be resolved
                    {
                        Logger.LogError("Could not resolve record for array member object " + objFormKey.Value.ToString());
                    }
                }
                else
                {
                    comparisonObject = GetObjectAtPath(candidateObj, subPath, objectLinkMap, linkCache);
                    if (comparisonObject != null && Eval.Execute<bool>(expression, comparisonObject)) // duplicated from above because compiler complains about unassigned local variable comparisonObject if I compare it outside the if/else.
                    {
                        return candidateObj;
                    }
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

        public static dynamic GetSubObject(Object root, string propertyName)
        {
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                return property.GetValue(root);
            }
            else
            {
                return null;
            }
        }

        public static void SetSubObject(dynamic root, string propertyName, dynamic value)
        {
            var property = root.GetType().GetProperty(propertyName);
            if (property != null)
            {
                property.SetValue(root, value);
            }
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

    public class ParsedPathObj
    {
        public ParsedPathObj()
        {
            this.CurrentObj = null;
            this.RemainingPath = new List<string>();
        }

        public object CurrentObj { get; set; }
        public List<string> RemainingPath { get; set; }
    }
}
