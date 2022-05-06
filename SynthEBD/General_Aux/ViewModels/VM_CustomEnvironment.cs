using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using System.Windows.Forms;
using Noggog.WPF;

namespace SynthEBD;

public class VM_CustomEnvironment : ViewModel
{
    public VM_CustomEnvironment(Window_CustomEnvironment window)
    {
        SelectCustomGameFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFile("", "Executable files (*.exe)|*.exe", "Select your game executable", out var gamePath) && !string.IsNullOrWhiteSpace(gamePath))
                {
                    CustomGamePath = gamePath;
                    var gameDir = System.IO.Path.GetDirectoryName(CustomGamePath);
                    var testDir = System.IO.Path.Combine(gameDir, "data");
                    Cursor.Current = Cursors.WaitCursor;
                    try
                    {
                        IsValidated = false;
                        Environment = PatcherEnvironment.BuildCustomEnvironment(CustomGamePath, SkyrimRelease);
                        Validate();
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.DisplayNotificationOK("Invalid Environment", "Environment creation failed with error:" + System.Environment.NewLine + ExceptionLogger.GetExceptionStack(ex, ""));
                    }
                    Cursor.Current = Cursors.Default;
                }
            }
        );

        OK = new RelayCommand(
            canExecute: _ => IsValidated,
            execute: _ =>
            {
                window.Close();
            }
        );

        Exit = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                IsValidated = false; // set false even if there is a valid environment so that calling function exits the application
                window.Close();
            }
        );
    }
    public string CustomGamePath { get; set; } = "";
    public bool IsValidated { get; set; } = false;
    public SkyrimRelease SkyrimRelease { get; set; } = SkyrimRelease.SkyrimSE;
    public RelayCommand SelectCustomGameFolder { get; set; }
    public RelayCommand OK { get; set; }
    public RelayCommand Exit { get; set; }
    public IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> Environment { get; set; }

    private void Validate()
    {
        var gameDir = System.IO.Path.GetDirectoryName(CustomGamePath);
        var dataDir = System.IO.Path.Combine(gameDir, "data");
        var validationPath = System.IO.Path.Combine(dataDir, "Skyrim.esm");
        if (System.IO.File.Exists(validationPath))
        {
            IsValidated = true;
        }
        else
        {
            CustomMessageBox.DisplayNotificationOK("Invalid Environment", "The chosen executable does not appear to be a Skyrim release.");
        }
    }
}