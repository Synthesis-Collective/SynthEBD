using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Shell;

namespace SynthEBD;

/// <summary>Contract a view model must implement to receive record-path autocomplete from
/// <see cref="RecordIntellisense"/>: it exposes the path being edited, a reference NPC and link cache to
/// resolve against, the current suggestion list, and the user's chosen suggestion.</summary>
public interface IImplementsRecordIntellisense
{
    /// <summary>The suggestion the user picked from the dropdown; setting it appends to the path.</summary>
    public RecordIntellisense.PathSuggestion ChosenPathSuggestion { get; set; }
    /// <summary>The current list of candidate sub-paths for the partially typed path.</summary>
    public ObservableCollection<RecordIntellisense.PathSuggestion> PathSuggestions { get; set; }
    /// <summary>FormKey of the NPC whose record the path is evaluated against to enumerate members.</summary>
    public FormKey ReferenceNPCFormKey { get; set; }
    /// <summary>The record path currently being edited (dot-delimited, with <c>[*]</c> for collections).</summary>
    public string IntellisensedPath { get; set; }
    /// <summary>Link cache used to resolve <see cref="ReferenceNPCFormKey"/>.</summary>
    public ILinkCache LinkCache { get; }
}
/// <summary>Provides record-path autocomplete for record-replacer/path editors: as the user types a
/// dotted path, it resolves the object at that path on a reference NPC and offers that object's
/// properties as the next path segment, auto-appending <c>[*]</c> for enumerable members.</summary>
public class RecordIntellisense : VM
{
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;
    public RecordIntellisense(RecordPathParser recordPathParser, Logger logger)
    {
        _recordPathParser = recordPathParser;
        _logger = logger;
    }
    /// <summary>Wires reactive subscriptions on <paramref name="parent"/>: refreshes suggestions when the
    /// reference NPC or path changes, and appends to the path when a suggestion is chosen. Subscriptions
    /// are disposed with this instance.</summary>
    public void InitializeSubscriptions(IImplementsRecordIntellisense parent)
    {
        ReactiveUI.WhenAnyMixin.WhenAnyValue(parent, x => x.ReferenceNPCFormKey, x => x.IntellisensedPath).Subscribe(_ => RefreshPathSuggestions(parent)).DisposeWith(this);
        ReactiveUI.WhenAnyMixin.WhenAnyValue(parent, vm => vm.ChosenPathSuggestion).Skip(1).WhereNotNull().Subscribe(pathSuggestion => UpdatePath(parent)).DisposeWith(this);
    }
    /// <summary>Rebuilds <see cref="IImplementsRecordIntellisense.PathSuggestions"/> by resolving the
    /// object at the current path (with <c>[*]</c> treated as index 0 and any trailing dot trimmed) on the
    /// reference NPC, then listing that object's properties as alphabetically sorted suggestions. Methods
    /// are intentionally not suggested.</summary>
    public void RefreshPathSuggestions(IImplementsRecordIntellisense parent)
    {
        parent.ChosenPathSuggestion = null; // clear this now to avoid the previous chosen path suggestion being added by the Subscription due to the current PathSuggestions being modified

        var tmpPath = parent.IntellisensedPath.Replace("[*]", "[0]"); // evaluate the first member of any collection to determine subpaths

        if (tmpPath.EndsWith('.'))
        {
            tmpPath = tmpPath.Remove(tmpPath.Length - 1, 1);
        }

        if (parent is null || parent.LinkCache is null) { return; }

        HashSet<PathSuggestion> newSuggestions = new();
        if (parent.LinkCache.TryResolve<INpcGetter>(parent.ReferenceNPCFormKey, out var referenceNPC) && _recordPathParser.GetObjectAtPath(referenceNPC, referenceNPC, tmpPath, new Dictionary<string, dynamic>(), parent.LinkCache, true, _logger.GetNPCLogNameString(referenceNPC), out var subObj))
        {
            Type type = subObj.GetType();
            var properties = type.GetProperties();
            foreach (var property in properties)
            {
                var newPathSuggestion = new PathSuggestion() { Parent = parent, SubPath = property.Name, PropInfo = property, Type = PathSuggestion.PathType.Property, SubPathType = type, SubObject = (object)subObj };
                newPathSuggestion.FinalizeSuggestion();
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

    /// <summary>A single autocomplete candidate: the path fragment to append plus a display label and the
    /// reflection metadata (property or method) it was derived from.</summary>
    public class PathSuggestion
    {
        /// <summary>The text appended to the path when this suggestion is chosen.</summary>
        public string SubPath { get; set; } = "";
        /// <summary>The label shown in the dropdown (member name plus type).</summary>
        public string DispString { get; set; } = "";
        /// <summary>The owning intellisense host this suggestion was generated for.</summary>
        public IImplementsRecordIntellisense Parent { get; set; }
        /// <summary>Whether this suggestion is a property or a method.</summary>
        public PathType Type { get; set; } = PathType.Property;
        /// <summary>Reflection info when this suggestion is a property.</summary>
        public PropertyInfo PropInfo { get; set; }
        /// <summary>Reflection info when this suggestion is a method.</summary>
        public MethodInfo MethInfo { get; set; }
        /// <summary>The declaring type at the resolved path.</summary>
        public Type SubPathType { get; set; }
        /// <summary>The resolved object at the current path.</summary>
        public object SubObject { get; set; }

        /// <summary>Whether a <see cref="PathSuggestion"/> describes a property or a method.</summary>
        public enum PathType
        {
            Property,
            Method
        }

        /// <summary>Populates <see cref="SubPath"/> and <see cref="DispString"/> from the property or
        /// method metadata according to <see cref="Type"/> (methods get a formatted parameter list).</summary>
        public void FinalizeSuggestion()
        {
            switch (Type)
            {
                case PathType.Property:
                    SubPath = PropInfo.Name;
                    DispString = PropInfo.Name + " (" + PropInfo.PropertyType.Name + ")";
                    break;
                case PathType.Method:
                    SubPath = MethInfo.Name + "(";
                    var parameters = MethInfo.GetParameters();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
                        if (param.IsOut) { SubPath += "out "; }
                        SubPath += param.ParameterType.Name + " " + param.Name;
                        if (i < parameters.Length - 1)
                        {
                            SubPath += ", ";
                        }
                    }
                    SubPath += ")";

                    DispString = "(Method) " + SubPath + " (" + MethInfo.ReturnType.Name + ")";
                    break;
            }
        }
    }

    /// <summary>Returns whether <paramref name="obj"/> should be treated as an enumerable for path
    /// purposes (i.e. exposes an indexer), explicitly excluding <see cref="string"/> and Mutagen's
    /// <c>GenderedItem&lt;T&gt;</c>.</summary>
    private static bool IsEnumerable(dynamic obj)
    {
        Type type = obj.GetType();
        //explicitly check for string because it is an IEnumerable but should not be treated as an array of chars
        if (type == typeof(string) || type.Name == "GenderedItem`1") { return false; }

        var properties = type.GetProperties();
        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Any())
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Appends the chosen suggestion's <see cref="PathSuggestion.SubPath"/> to the host's path
    /// (inserting a dot separator as needed), then appends <c>[*]</c> if the newly resolved object is
    /// enumerable, and finally clears the chosen suggestion to reset the dropdown.</summary>
    public void UpdatePath(IImplementsRecordIntellisense parent)
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

        if (parent.LinkCache.TryResolve<INpcGetter>(parent.ReferenceNPCFormKey, out var referenceNPC) && _recordPathParser.GetObjectAtPath(referenceNPC, referenceNPC, parent.IntellisensedPath, new Dictionary<string, dynamic>(), parent.LinkCache, true, _logger.GetNPCLogNameString(referenceNPC), out var subObj))
        {
            if (IsEnumerable(subObj))
            {
                parent.IntellisensedPath += "[*]";
            }
        }

        parent.ChosenPathSuggestion = new PathSuggestion(); // clear the dropdown box
    }
}