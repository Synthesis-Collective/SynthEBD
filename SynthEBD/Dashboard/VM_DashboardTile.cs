namespace SynthEBD;

/// <summary>Visual state of a dashboard tile's indicator dot.</summary>
public enum TileHealth
{
    /// <summary>Module is off / has no content (dimmed).</summary>
    Off,
    /// <summary>Module is active and healthy.</summary>
    Ok,
    /// <summary>Module needs attention (e.g. invalid environment).</summary>
    Warning,
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

    public VM_DashboardTile(string title, bool hasPowerToggle, Func<string> statusProvider,
        Func<TileHealth> healthProvider, Action navigate)
    {
        Title = title;
        HasPowerToggle = hasPowerToggle;
        _statusProvider = statusProvider;
        _healthProvider = healthProvider;
        Open = new RelayCommand(canExecute: _ => true, execute: _ => navigate());
        Refresh();
    }

    public string Title { get; }
    public bool HasPowerToggle { get; }
    /// <summary>Two-way bridge to the module's enable flag; the dashboard wires both directions.</summary>
    public bool IsPowered { get; set; }
    public string StatusText { get; set; } = "";
    public TileHealth Health { get; set; } = TileHealth.Off;
    public RelayCommand Open { get; }

    /// <summary>Recomputes the status readout and health dot from the provider delegates.</summary>
    public void Refresh()
    {
        StatusText = _statusProvider();
        Health = _healthProvider();
    }
}
