using Noggog;
using System.Security.Cryptography;
using System.IO;
using System.Security;

namespace SynthEBD;

/// <summary>Assorted stateless string, path, hashing, and matrix-preprocessing helpers.</summary>
public class MiscFunctions
{
    /// <summary>Determines whether two string sets contain the same values, ignoring case.</summary>
    /// <param name="a">First set.</param>
    /// <param name="b">Second set.</param>
    /// <returns><c>true</c> if the sets have equal counts and every member of <paramref name="a"/> is present in <paramref name="b"/> case-insensitively.</returns>
    /// <remarks>Because the inputs are case-sensitive <see cref="HashSet{T}"/>s, sets containing case-variant duplicates (e.g. "X" and "x") can compare equal to a differently-populated set; see review notes.</remarks>
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
    /// <summary>Replaces the last occurrence of a substring within a string.</summary>
    /// <param name="Source">The string to search.</param>
    /// <param name="Find">The substring to find.</param>
    /// <param name="Replace">The replacement text.</param>
    /// <returns><paramref name="Source"/> with the final occurrence of <paramref name="Find"/> replaced, or unchanged if not found.</returns>
    public static string ReplaceLastOccurrence(string Source, string Find, string Replace)
    {
        int place = Source.LastIndexOf(Find);

        if (place == -1)
            return Source;

        return Source.Remove(place, Find.Length).Insert(place, Replace);
    }

    /// <summary>Returns a copy of the input containing only its letter and digit characters.</summary>
    /// <param name="input">The string to filter.</param>
    /// <returns>The input with all non-alphanumeric characters removed.</returns>
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

    /// <summary>Sanitizes a string into a token usable as an XML element name.</summary>
    /// <param name="input">The raw string (e.g. a descriptor or subgroup label).</param>
    /// <returns>A tag-safe token: '+' becomes "p-", an empty/whitespace input becomes "_", a leading digit is prefixed with "_", and spaces become underscores.</returns>
    /// <remarks>Only these specific cases are handled; other characters invalid in an XML NCName are not escaped.</remarks>
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

    // Function to center the data
    /// <summary>Mean-centers each column of an integer matrix (subtracts the column mean from every element).</summary>
    /// <param name="data">Row-major 2-D sample matrix (rows = samples, columns = features).</param>
    /// <returns>A new matrix whose columns each have zero mean.</returns>
    /// <remarks>A preprocessing step (e.g. before standardization/PCA) for the ML-based BodySlide classifier.</remarks>
    public static double[,] CenterData(int[,] data)
    {
        int numRows = data.GetLength(0);
        int numCols = data.GetLength(1);

        double[,] centeredData = new double[numRows, numCols];

        for (int j = 0; j < numCols; j++)
        {
            int sum = 0;

            for (int i = 0; i < numRows; i++)
            {
                sum += data[i, j];
            }

            double mean = (double)sum / numRows;

            for (int i = 0; i < numRows; i++)
            {
                centeredData[i, j] = data[i, j] - mean;
            }
        }

        return centeredData;
    }

    // Function to standardize the data
    /// <summary>Scales each column of a matrix by its root-mean-square so columns share a common scale.</summary>
    /// <param name="data">Row-major 2-D matrix, expected to be mean-centered first (see <see cref="CenterData"/>).</param>
    /// <returns>A new matrix with each column divided by its RMS.</returns>
    /// <remarks>When the input is already centered, the per-column RMS equals the standard deviation, making this z-score standardization.</remarks>
    public static double[,] StandardizeData(double[,] data)
    {
        int numRows = data.GetLength(0);
        int numCols = data.GetLength(1);

        double[,] standardizedData = new double[numRows, numCols];

        for (int j = 0; j < numCols; j++)
        {
            double sumSquared = 0;

            for (int i = 0; i < numRows; i++)
            {
                sumSquared += data[i, j] * data[i, j];
            }

            double stdDev = Math.Sqrt(sumSquared / numRows);

            for (int i = 0; i < numRows; i++)
            {
                standardizedData[i, j] = data[i, j] / stdDev;
            }
        }

        return standardizedData;
    }

    //https://stackoverflow.com/a/10520086
    /// <summary>Computes the lowercase hex MD5 hash of a file's contents.</summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <returns>The 32-character lowercase hexadecimal MD5 digest.</returns>
    /// <remarks>Used for content-identity checks (e.g. detecting duplicate/changed assets), not security.</remarks>
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