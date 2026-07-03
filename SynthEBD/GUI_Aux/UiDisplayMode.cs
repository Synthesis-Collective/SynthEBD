namespace SynthEBD;

/// <summary>
/// Progressive-disclosure level for the UI, selected by the [Use][Customize][Troubleshoot]
/// toggle in the main window. Menus show an element when the active mode is at or above the
/// element's minimum level, so the enum order is meaningful (each level is a superset).
/// </summary>
public enum UiDisplayMode
{
    /// <summary>Browse and use installed content only (typical end user; no distribution rules).</summary>
    Use = 0,
    /// <summary>Full customization: distribution rules, groupings, integration options.</summary>
    Customize = 1,
    /// <summary>In-the-weeds diagnostics and compatibility switches (formerly "troubleshooting settings").</summary>
    Troubleshoot = 2,
}
