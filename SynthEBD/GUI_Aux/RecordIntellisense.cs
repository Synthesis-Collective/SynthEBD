using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Shell;

namespace SynthEBD;

public interface IImplementsRecordIntellisense
{
    public RecordIntellisense.PathSuggestion ChosenPathSuggestion { get; set; }
    public ObservableCollection<RecordIntellisense.PathSuggestion> PathSuggestions { get; set; }
    public FormKey ReferenceNPCFormKey { get; set; }
    public string IntellisensedPath { get; set; }
    public int IntellisensedPathCaretPosition { get; set; }
    public int IntellisenseManualRefreshTrigger { get; set; }
    public ILinkCache LinkCache { get; }
}
public class RecordIntellisense : VM
{
    private readonly RecordPathParser _recordPathParser;
    private readonly Logger _logger;
    public RecordIntellisense(RecordPathParser recordPathParser, Logger logger)
    {
        _recordPathParser = recordPathParser;
        _logger = logger;
    }
    public void InitializeSubscriptions(IImplementsRecordIntellisense parent)
    {
        parent.IntellisenseManualRefreshTrigger = 1;
        parent.WhenAnyValue(x => x.ReferenceNPCFormKey).Subscribe(x => RefreshPathSuggestions(parent)).DisposeWith(this);
        parent.WhenAnyValue(x => x.IntellisensedPath).Subscribe(x => RefreshPathSuggestions(parent)).DisposeWith(this);
        parent.WhenAnyValue(x => x.IntellisenseManualRefreshTrigger).Subscribe(x => RefreshPathSuggestions(parent)).DisposeWith(this);
        parent.WhenAnyValue(vm => vm.ChosenPathSuggestion).Skip(1).WhereNotNull().Subscribe(pathSuggestion => UpdatePath(parent)).DisposeWith(this);
    }
    public void RefreshPathSuggestions(IImplementsRecordIntellisense parent)
    {
        parent.ChosenPathSuggestion = null; // clear this now to avoid the previous chosen path suggestion being added by the Subscription due to the current PathSuggestions being modified

        var tmpPath = parent.IntellisensedPath.Replace("[*]", "[0]"); // evaluate the first member of any collection to determine subpaths

        if (parent.IntellisensedPathCaretPosition > 0 && parent.IntellisensedPathCaretPosition < parent.IntellisensedPath.Length)
        {
            var substring = GetWidestScopeSubstring(parent.IntellisensedPath, parent.IntellisensedPathCaretPosition, out int closingBracketPos);
            if (substring.IsNullOrWhitespace() && closingBracketPos > 0)
            {
                tmpPath = parent.IntellisensedPath.Substring(0, closingBracketPos + 1);
            }    
        }

        tmpPath = tmpPath.Replace("[]", "[0]"); // evaluate the first member of any collection to determine subpaths

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

    static string GetWidestScopeSubstring(string input, int startIndex, out int closingBracketIndex)
    {
        //startIndex += 1; // adjust to account for cursor position being within bracket
        closingBracketIndex = -1;

        while (startIndex >= 0 && input[startIndex] != '[')
        {
            startIndex -= 1;
        }

        var debugStartIndexChar = input[startIndex];

        if (startIndex < 0 || startIndex >= input.Length || input[startIndex] != '[')
        {
            // Invalid starting index or no opening bracket at the starting index
            return string.Empty;
        }

        int bracketCount = 0;
        int endIndex = startIndex;

        // Find the matching closing bracket for the starting bracket
        while (endIndex < input.Length)
        {
            var debugEndIndexChar = input[endIndex];
            if (input[endIndex] == '[')
            {
                bracketCount++;
            }
            else if (input[endIndex] == ']')
            {
                bracketCount--;
                if (bracketCount == 0)
                {
                    // Found the matching closing bracket
                    closingBracketIndex = endIndex;
                    break;
                }
            }

            endIndex++;

            if (bracketCount == 0)
            {
                // Exit the loop if the closing bracket is found
                break;
            }
        }

        if (bracketCount != 0)
        {
            // Unmatched brackets
            return string.Empty;
        }

        // Extract the substring between the starting and ending brackets
        return input.Substring(startIndex + 1, endIndex - startIndex - 1);
    }

    public class PathSuggestion
    {
        public string SubPath { get; set; } = "";
        public string DispString { get; set; } = "";
        public IImplementsRecordIntellisense Parent { get; set; }
        public PathType Type { get; set; } = PathType.Property;
        public PropertyInfo PropInfo { get; set; }
        public MethodInfo MethInfo { get; set; }
        public Type SubPathType { get; set; }
        public object SubObject { get; set; }

        public enum PathType
        {
            Property,
            Method
        }

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

    public void UpdatePath(IImplementsRecordIntellisense parent)
    {
        if (parent.ChosenPathSuggestion is null || parent.ChosenPathSuggestion.DispString == "") { return; }

        var insertionPos = parent.IntellisensedPathCaretPosition;

        if (insertionPos == 0)
        {
            insertionPos = parent.IntellisensedPath.Length;
        }

        if (parent.IntellisensedPath.Length > 0 && parent.IntellisensedPath[insertionPos - 1] != '.' && parent.IntellisensedPath[insertionPos - 1] != '[')
        {
            parent.IntellisensedPath = parent.IntellisensedPath.Insert(insertionPos, "." + parent.ChosenPathSuggestion.SubPath);
        }
        else
        {
            parent.IntellisensedPath = parent.IntellisensedPath.Insert(insertionPos, parent.ChosenPathSuggestion.SubPath);
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