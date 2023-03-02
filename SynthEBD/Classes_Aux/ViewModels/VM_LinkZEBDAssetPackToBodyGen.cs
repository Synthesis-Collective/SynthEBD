namespace SynthEBD;

public class VM_LinkZEBDAssetPackToBodyGen : VM
{
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

        OKcommand = new SynthEBD.RelayCommand(
            canExecute: _ => true,
            execute: _ => AssociatedWindow.Close()
        );

        ClearCommand = new SynthEBD.RelayCommand(
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