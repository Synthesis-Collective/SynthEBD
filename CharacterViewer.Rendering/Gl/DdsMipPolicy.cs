using System.Globalization;

namespace CharacterViewer.Rendering;

/// <summary>Eligibility for authored DDS mips, independent of texture dimensions at level zero.</summary>
public static class DdsMipPolicy
{
    public const string EnvironmentVariable = "CVR_DDS_MAX_FINAL_MIP_DIMENSION";

    /// <summary>Null/empty/decoded disables compressed uploads; unlimited accepts any valid chain.
    /// A positive integer limits max(width, height) of the last supplied mip.</summary>
    public static int? Parse(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value) || string.Equals(value, "decoded", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(value, "unlimited", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0) return size;
        throw new ArgumentException("DDS final mip dimension must be a positive integer, unlimited, or decoded.", nameof(value));
    }
}
