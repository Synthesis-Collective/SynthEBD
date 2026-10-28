using System.Globalization;

namespace SkyRender;

/// <summary>Eligibility for authored DDS mips, independent of texture dimensions at level zero.</summary>
public static class DdsMipPolicy
{
    public const string EnvironmentVariable = "SKYRENDER_DDS_MAX_FINAL_MIP_DIMENSION";

    /// <summary>Former name of <see cref="EnvironmentVariable"/>, still honoured when the new
    /// variable is unset.</summary>
    public const string LegacyEnvironmentVariable = "CVR_DDS_MAX_FINAL_MIP_DIMENSION";

    /// <summary>The configured value: <see cref="EnvironmentVariable"/> when set, otherwise
    /// <see cref="LegacyEnvironmentVariable"/>, otherwise null. Pass the result to <see cref="Parse"/>.</summary>
    public static string? ReadEnvironmentValue() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable)
        ?? Environment.GetEnvironmentVariable(LegacyEnvironmentVariable);

    /// <summary>The policy when nothing is configured: authored-mip compressed uploads for chains whose
    /// last mip is at most 4 px. 4 admitted 579 of 586 textures in the threshold validation with the
    /// same captured pixels as unlimited, without admitting large single-level images.</summary>
    public const int DefaultMaximumFinalMipDimension = 4;

    /// <summary>Null/empty selects <see cref="DefaultMaximumFinalMipDimension"/>; "decoded" opts out
    /// (decoded pixels, generated mips); "unlimited" accepts any valid chain. A positive integer
    /// limits max(width, height) of the last supplied mip.</summary>
    public static int? Parse(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return DefaultMaximumFinalMipDimension;
        if (string.Equals(value, "decoded", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(value, "unlimited", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0) return size;
        throw new ArgumentException("DDS final mip dimension must be a positive integer, unlimited, or decoded.", nameof(value));
    }
}
