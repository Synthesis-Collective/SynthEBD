using Noggog;
using System.Security.Cryptography;
using System.IO;
using System.Security;

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

    //https://stackoverflow.com/a/10520086
    public static string CalculateMD5(string filePath)
    {
        using (var md5 = MD5.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    //https://stackoverflow.com/a/41049011
    /// <summary>
    /// Gets a value that indicates whether <paramref name="path"/>
    /// is a valid path.
    /// </summary>
    /// <returns>Returns <c>true</c> if <paramref name="path"/> is a
    /// valid path; <c>false</c> otherwise. Also returns <c>false</c> if
    /// the caller does not have the required permissions to access
    /// <paramref name="path"/>.
    /// </returns>
    /// <seealso cref="Path.GetFullPath"/>
    /// <seealso cref="TryGetFullPath"/>
    public static bool IsValidPath(string path)
    {
        string result;
        return TryGetFullPath(path, out result);
    }

    /// <summary>
    /// Returns the absolute path for the specified path string. A return
    /// value indicates whether the conversion succeeded.
    /// </summary>
    /// <param name="path">The file or directory for which to obtain absolute
    /// path information.
    /// </param>
    /// <param name="result">When this method returns, contains the absolute
    /// path representation of <paramref name="path"/>, if the conversion
    /// succeeded, or <see cref="String.Empty"/> if the conversion failed.
    /// The conversion fails if <paramref name="path"/> is null or
    /// <see cref="String.Empty"/>, or is not of the correct format. This
    /// parameter is passed uninitialized; any value originally supplied
    /// in <paramref name="result"/> will be overwritten.
    /// </param>
    /// <returns><c>true</c> if <paramref name="path"/> was converted
    /// to an absolute path successfully; otherwise, false.
    /// </returns>
    /// <seealso cref="Path.GetFullPath"/>
    /// <seealso cref="IsValidPath"/>
    public static bool TryGetFullPath(string path, out string result)
    {
        result = String.Empty;
        if (String.IsNullOrWhiteSpace(path)) { return false; }
        bool status = false;

        try
        {
            result = Path.GetFullPath(path);
            status = true;
        }
        catch (ArgumentException) { }
        catch (SecurityException) { }
        catch (NotSupportedException) { }
        catch (PathTooLongException) { }

        if (status)
        {
            status = !ContainsInvalidPathCharacters(path);
        }

        return status;
    }

    //https://stackoverflow.com/a/34148976
    /// <summary>Determines if the path contains invalid characters.</summary>
    /// <remarks>This method is intended to prevent ArgumentException's from being thrown when creating a new FileInfo on a file path with invalid characters.</remarks>
    /// <param name="filePath">File path.</param>
    /// <returns>True if file path contains invalid characters.</returns>
    private static bool ContainsInvalidPathCharacters(string filePath)
    {
        for (var i = 0; i < filePath.Length; i++)
        {
            int c = filePath[i];

            if (c == '\"' || c == '<' || c == '>' || c == '|' || c == '*' || c == '?' || c < 32)
                return true;
        }

        return false;
    }
}