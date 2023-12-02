using Noggog;

namespace SynthEBD;

public class MiscFunctions
{
    public static bool StringHashSetsEqualCaseInvariant(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var s in a)
        {
            if (!b.Contains(s, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    //https://stackoverflow.com/a/14826068
    public static string ReplaceLastOccurrence(string Source, string Find, string Replace)
    {
        int place = Source.LastIndexOf(Find);

        if (place == -1)
            return Source;

        return Source.Remove(place, Find.Length).Insert(place, Replace);
    }

    public static string MakeAlphanumeric(string input)
    {
        string output = string.Empty;
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                output += c;
            }
        }
        return output;
    }

    public static string MakeXMLtagCompatible(string input)
    {
        if (input.Contains('+'))
        {
            input = input.Replace("+", "p-");
        }

        if (input.IsNullOrWhitespace())
        {
            return "_";
        }

        if (char.IsDigit(input.First()))
        {
            input = "_" + input;
        }

        return input.Replace(' ', '_');
    }
}