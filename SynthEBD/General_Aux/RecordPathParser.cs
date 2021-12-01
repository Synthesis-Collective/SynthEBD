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

namespace SynthEBD
{
    public class RecordPathParser
    {
        public static Object GetObjectAtPath(Object rootObj, string relativePath, Dictionary<string, Object> objectCache, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            string[] splitPath = SplitPath(relativePath);
            Object currentObj = rootObj;

            for (int i = 0; i < splitPath.Length; i++)
            {
                if (currentObj == null)
                {
                    return null;
                }
                string currentSubPath = splitPath[i];

                // handle subrecords
                if (PropertyIsRecord(currentObj, currentSubPath, out FormKey? subrecordFK))
                {
                    if (subrecordFK != null && !subrecordFK.Value.IsNull && linkCache.TryResolve(subrecordFK.Value, out var subRecordGetter))
                    {
                        currentObj = subRecordGetter;
                    }
                    else
                    {
                        return null;
                    }
                }

                // handle arrays
                else if (PathIsArray(currentSubPath, out string arraySubPath, out string arrIndex))
                {
                    var collectionObj = (IEnumerable<object>)GetObjectAtPath(currentObj, arraySubPath, objectCache, linkCache);

                    //if array index is numeric
                    if (int.TryParse(arrIndex, out int iIndex))
                    {
                        if (iIndex < collectionObj.Count())
                        {
                            currentObj = collectionObj.ElementAt(iIndex);
                        }
                        else
                        {
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
                        currentObj = ChooseWhichArrayObject(collectionObj, subPath, matchCondition, linkCache);
                        if (currentObj == null)
                        {
                            Logger.LogError("Could not get object at " + currentSubPath + " because " + arraySubPath + " does not have an element that matches condition: " + arrIndex);
                            return null;
                        }
                    }
                }

                // if object is not an array
                else
                {
                    currentObj = GetSubObject(currentObj, currentSubPath);
                }
            }

            return currentObj;
        }

        private static object ChooseWhichArrayObject(IEnumerable<object> variants, string subPath, string matchCondition, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            foreach (var candidateObj in variants)
            {
                dynamic comparisonObject;
                if (ObjectIsRecord(candidateObj, out var linkedFormKey) && linkedFormKey != null && linkedFormKey.Value.IsNull == false && linkCache.TryResolve(linkedFormKey.Value, out var candidateObjRecord))
                {
                    comparisonObject = GetObjectAtPath(candidateObjRecord, subPath, new Dictionary<string, object>(), linkCache);
                }
                else
                {
                    comparisonObject = GetObjectAtPath(candidateObj, subPath, new Dictionary<string, object>(), linkCache);
                }

                string expression = "comparisonObject." + matchCondition;

                if (Eval.Execute<bool>(expression))
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

        public static Object GetSubObject(Object root, string propertyName)
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

        public static void SetSubObject(Object root, string propertyName, Object value)
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
        public static bool PropertyIsRecord(Object root, string propertyName, out FormKey? formKey)
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

        public static bool PropertyIsRecord(Object root, string propertyName)
        {
            var property = root.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType.Name.StartsWith("IFormLinkNullableGetter"))
            {
                return true;
            }
            return false;
        }

        public static bool ObjectIsRecord(Object obj, out FormKey? formKey)
        {
            var property = obj.GetType().GetProperty("FormKey");
            if (property != null)
            {
                formKey = (FormKey)property.GetValue(obj);
                return true;
            }

            formKey = null;
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
