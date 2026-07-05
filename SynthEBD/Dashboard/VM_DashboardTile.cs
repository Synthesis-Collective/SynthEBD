namespace SynthEBD;

/// <summary>Visual state of a dashboard tile's indicator dot (and optional footer text).</summary>
public enum TileHealth
{
    /// <summary>Module is off / has no content (dimmed).</summary>
    Off,
    /// <summary>Module is active and healthy (green).</summary>
    Ok,
    /// <summary>Module needs attention, e.g. invalid environment or no mod manager set (yellow).</summary>
    Warning,
    /// <summary>A configured value is broken, e.g. a mod-manager path that does not exist (red).</summary>
    Error,
}

/// <summary>
/// One tile on the <see cref="VM_Dashboard"/>: a title, an optional power toggle bridged to the
/// underlying module's enable flag (wired by the dashboard), a short status readout, and a click
/// command that navigates to the module's detail menu. Status and health are recomputed through
/// the provider delegates whenever the dashboard becomes visible.
/// </summary>
public class VM_DashboardTile : VM
{
    private readonly Func<string> _statusProvider;
    private readonly Func<TileHealth> _healthProvider;
    private readonly Func<(string Text, TileHealth Health)>? _footerProvider;

    public VM_DashboardTile(string title, string docKey, bool hasPowerToggle, Func<string> statusProvider,
        Func<TileHealth> healthProvider, Action navigate,
        string? footerLabel = null, string? footerDocKey = null,
        Func<(string Text, TileHealth Health)>? footerProvider = null)
    {
        Title = title;
        DocKey = docKey;
        HasPowerToggle = hasPowerToggle;
        _statusProvider = statusProvider;
        _healthProvider = healthProvider;
        FooterLabel = footerLabel;
        FooterDocKey = footerDocKey;
        _footerProvider = footerProvider;
        Open = new RelayCommand(canExecute: _ => true, execute: _ => navigate());
        Refresh();
    }

    public string Title { get; }
    /// <summary>UiDocs key for the tile's rich tooltip (bound by the shared tile template).</summary>
    public string DocKey { get; }
    public bool HasPowerToggle { get; }
    /// <summary>Two-way bridge to the module's enable flag; the dashboard wires both directions.</summary>
    public bool IsPowered { get; set; }
    public string StatusText { get; set; } = "";
    public TileHealth Health { get; set; } = TileHealth.Off;
    public RelayCommand Open { get; }

    // --- Optional colored footer line (used by the Environment tile for mod-manager status) ---
    /// <summary>Neutral prefix rendered before <see cref="FooterText"/>, e.g. "Mod Manager: ". Null on tiles without a footer.</summary>
    public string? FooterLabel { get; }
    /// <summary>UiDocs key for the footer's own rich tooltip; null leaves the footer with no tooltip.</summary>
    public string? FooterDocKey { get; }
    /// <summary>Footer value shown after the label and tinted by <see cref="FooterHealth"/>. Empty when the tile has no footer.</summary>
    public string? FooterText { get; set; }
    /// <summary>Color state for <see cref="FooterText"/>: Ok=green, Warning=yellow, Error=red.</summary>
    public TileHealth FooterHealth { get; set; } = TileHealth.Off;
    /// <summary>True when a footer line should be shown (a footer provider supplied non-empty text).</summary>
    public bool HasFooter { get; set; }

    /// <summary>Recomputes the status readout, health dot, and optional footer from the provider delegates.</summary>
    public void Refresh()
    {
        StatusText = _statusProvider();
        Health = _healthProvider();
        if (_footerProvider != null)
        {
            var (text, health) = _footerProvider();
            FooterText = text;
            FooterHealth = health;
            HasFooter = !string.IsNullOrEmpty(text);
        }
    }
}
