using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public interface IImplementsRecordIntellisense
    {
        public RecordIntellisense.PathSuggestion ChosenPathSuggestion { get; set; }
        public ObservableCollection<RecordIntellisense.PathSuggestion> PathSuggestions { get; set; }
        public FormKey ReferenceNPCFormKey { get; set; }
        public string IntellisensedPath { get; set; }
        public ILinkCache LinkCache { get; set; }
    }
    public class RecordIntellisense
    {
        public static void InitializeSubscriptions(IImplementsRecordIntellisense parent)
        {
            parent.WhenAnyValue(x => x.ReferenceNPCFormKey).Subscribe(x => RefreshPathSuggestions(parent));
            parent.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => RefreshPathSuggestions(parent));
            parent.WhenAnyValue(vm => vm.ChosenPathSuggestion).Skip(1).WhereNotNull().Subscribe(pathSuggestion => UpdatePath(parent));
        }
        public static void RefreshPathSuggestions(IImplementsRecordIntellisense parent)
        {
            parent.ChosenPathSuggestion = null; // clear this now to avoid the previous chosen path suggestion being added by the Subscription due to the current PathSuggestions being modified

            var tmpPath = parent.IntellisensedPath.Replace("[*]", "[0]"); // evaluate the first member of any collection to determine subpaths

            if (tmpPath.EndsWith('.'))
            {
                tmpPath = tmpPath.Remove(tmpPath.Length - 1, 1);
            }

            if (parent is null || parent.LinkCache is null) { return; }

            HashSet<PathSuggestion> newSuggestions = new HashSet<PathSuggestion>();
            if (parent.LinkCache.TryResolve<INpcGetter>(parent.ReferenceNPCFormKey, out var referenceNPC) && RecordPathParser.GetObjectAtPath(referenceNPC, tmpPath, new Dictionary<dynamic, Dictionary<string, dynamic>>(), parent.LinkCache, true, Logger.GetNPCLogNameString(referenceNPC), out var subObj))
            {
                Type type = subObj.GetType();
                var properties = type.GetProperties();
                foreach (var property in properties)
                {
                    var newPathSuggestion = new PathSuggestion() { SubPath = property.Name, PropInfo = property, Type = PathSuggestion.PathType.Property, SubPathType = type, SubObject = (object)subObj };
                    PathSuggestion.FinalizePathSuggestion(newPathSuggestion);
                    newSuggestions.Add(newPathSuggestion);
                }
                /* Not implementing methods for now
                var methods = type.GetMethods();
                foreach (var method in methods)
                {
                    if (method.Name.StartsWith("get_")) { continue; }
                    var newPathSuggestion = new PathSuggestion() { SubPath = method.Name, MethInfo = method, Type = PathSuggestion.PathType.Method };
                    PathSuggestion.FinalizePathSuggestion(newPathSuggestion);
                    PathSuggestions.Add(newPathSuggestion);
                }
                */
            }

            parent.PathSuggestions = new ObservableCollection<PathSuggestion>(newSuggestions.OrderBy(x => x.DispString));
        }

        public class PathSuggestion
        {
            public PathSuggestion()
            {
                SubPath = "";
                DispString = "";
                Type = PathType.Property;
            }
            public string SubPath { get; set; }
            public string DispString { get; set; }
            public PathType Type { get; set; }
            public PropertyInfo PropInfo { get; set; }
            public MethodInfo MethInfo { get; set; }
            public Type SubPathType { get; set; }
            public object SubObject { get; set; }

            public enum PathType
            {
                Property,
                Method
            }

            public static void FinalizePathSuggestion(PathSuggestion input)
            {
                switch (input.Type)
                {
                    case PathType.Property:
                        input.SubPath = input.PropInfo.Name;
                        if (IsEnumerable(input.PropInfo.PropertyType))
                        {
                            input.SubPath += "[*]";
                        }

                        input.DispString = input.PropInfo.Name + " (" + input.PropInfo.PropertyType.Name + ")";
                        break;
                    case PathType.Method:
                        input.SubPath = input.MethInfo.Name + "(";
                        var parameters = input.MethInfo.GetParameters();
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var param = parameters[i];
                            if (param.IsOut) { input.SubPath += "out "; }
                            input.SubPath += param.ParameterType.Name + " " + param.Name;
                            if (i < parameters.Length - 1)
                            {
                                input.SubPath += ", ";
                            }
                        }
                        input.SubPath += ")";

                        input.DispString = "(Method) " + input.SubPath + " (" + input.MethInfo.ReturnType.Name + ")";
                        break;
                }
            }
        }

        private static bool IsEnumerable(Type type)
        {
            //explicitly check for string because it is an IEnumerable but should not be treated as an array of chars
            if (type == typeof(string)) { return false; }

            var containedInterfaces = type.GetInterfaces();
            if (containedInterfaces.Contains(typeof(System.Collections.IEnumerable)))
            {
                return true;
            }
            return false;
        }

        public static void UpdatePath(IImplementsRecordIntellisense parent)
        {
            if (parent.ChosenPathSuggestion is null || parent.ChosenPathSuggestion.DispString == "") { return; }
            if (parent.IntellisensedPath.Length > 0 && !parent.IntellisensedPath.EndsWith('.'))
            {
                parent.IntellisensedPath += "." + parent.ChosenPathSuggestion.SubPath;
            }
            else
            {
                parent.IntellisensedPath += parent.ChosenPathSuggestion.SubPath;
            }

            parent.ChosenPathSuggestion = new PathSuggestion(); // clear the dropdown box
        }
    }
}
