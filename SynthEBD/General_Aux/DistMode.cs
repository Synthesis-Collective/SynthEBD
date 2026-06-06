namespace SynthEBD;

/// <summary>
/// Statistical shape used when drawing a randomized value: evenly across the range,
/// or biased toward its center.
/// </summary>
public enum DistMode
{
    /// <summary>Flat distribution — every value in the range is equally likely.</summary>
    uniform,
    /// <summary>Bell-curve distribution — values near the middle of the range are favored.</summary>
    bellCurve
}