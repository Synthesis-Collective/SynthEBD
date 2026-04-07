using System.Text.RegularExpressions;

namespace SynthEBD;

/// <summary>
/// Represents a four-part program version string (e.g. "1.0.5.5") with support for ordered comparisons.
/// Implicit conversion from string allows writing: if (appliedVersion &lt; "1.0.2.5")
/// </summary>
public class ProgramVersion : IComparable<ProgramVersion>, IEquatable<ProgramVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Build { get; }
    public int Patch { get; }

    public ProgramVersion(string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            Major = 0; Minor = 0; Build = 0; Patch = 0;
            return;
        }

        var match = Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)\.(\d+)$");
        if (match.Success)
        {
            Major = int.Parse(match.Groups[1].Value);
            Minor = int.Parse(match.Groups[2].Value);
            Build = int.Parse(match.Groups[3].Value);
            Patch = int.Parse(match.Groups[4].Value);
        }
        else
        {
            // Fallback: split by '.' and parse what we can
            var parts = versionString.Split('.');
            Major = parts.Length > 0 && int.TryParse(parts[0], out var v0) ? v0 : 0;
            Minor = parts.Length > 1 && int.TryParse(parts[1], out var v1) ? v1 : 0;
            Build = parts.Length > 2 && int.TryParse(parts[2], out var v2) ? v2 : 0;
            Patch = parts.Length > 3 && int.TryParse(parts[3], out var v3) ? v3 : 0;
        }
    }

    public int CompareTo(ProgramVersion? other)
    {
        if (other is null) return 1;
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Build != other.Build) return Build.CompareTo(other.Build);
        return Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ProgramVersion a, ProgramVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(ProgramVersion a, ProgramVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(ProgramVersion a, ProgramVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ProgramVersion a, ProgramVersion b) => a.CompareTo(b) >= 0;
    public static bool operator ==(ProgramVersion? a, ProgramVersion? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(ProgramVersion? a, ProgramVersion? b) => !(a == b);

    /// <summary>Allows writing: ProgramVersion v = "1.0.5.5";</summary>
    public static implicit operator ProgramVersion(string versionString) => new(versionString);

    public bool Equals(ProgramVersion? other)
    {
        if (other is null) return false;
        return Major == other.Major && Minor == other.Minor && Build == other.Build && Patch == other.Patch;
    }

    public override bool Equals(object? obj) => obj is ProgramVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Build, Patch);
    public override string ToString() => $"{Major}.{Minor}.{Build}.{Patch}";
}
