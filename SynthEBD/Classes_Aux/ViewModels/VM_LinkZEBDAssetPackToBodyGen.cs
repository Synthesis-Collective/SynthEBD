namespace SynthEBD;

/// <summary>
/// View model for the modal prompt shown when upgrading a zEBD asset config to SynthEBD format, asking
/// the user which BodyGen config to associate with it.
/// </summary>
public class VM_LinkZEBDAssetPackToBodyGen : VM
{
    /// <summary>Creates the prompt VM, populating the available BodyGen configs for the given gender and wiring OK/Clear commands.</summary>
    /// <param name="availableConfigs">All BodyGen configs to choose from.</param>
    /// <param name="gender">Gender whose configs are offered.</param>
    /// <param name="assetPackLabel">Label of the asset pack being upgraded (for the prompt text).</param>
    /// <param name="associatedWindow">The hosting window (closed by the commands).</param>
    public VM_LinkZEBDAssetPackToBodyGen(BodyGenConfigs availableConfigs, Gender gender, string assetPackLabel, Window_LinkZEBDAssetPackToBodyGen associatedWindow)
    {
        AssociatedWindow = associatedWindow;
        AssociatedWindow.WindowStyle = System.Windows.WindowStyle.None; // hide title bar and close button
        DispString = "Attempting to upgrade " + assetPackLabel + " from zEBD Config to SynthEBD format. Which BodyGen Config should be associated with this config file?";
        switch (gender)
        {
            case Gender.Female: AvailableConfigs = availableConfigs.Female; break;
            case Gender.Male: AvailableConfigs = availableConfigs.Male; break;
        }

        if (AvailableConfigs.Count > 0) { SelectedConfig = AvailableConfigs.First(); }

        OKcommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedWindow.Close()
        );

        ClearCommand = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                SelectedConfig = null;
                AssociatedWindow.Close();
            }
        );
    }

    public string DispString { get; set; }
    public HashSet<BodyGenConfig> AvailableConfigs { get; set; }
    public BodyGenConfig SelectedConfig { get; set; } = null;
    public Window_LinkZEBDAssetPackToBodyGen AssociatedWindow { get; set; }
    public RelayCommand OKcommand { get; }
    public RelayCommand ClearCommand { get; }
}