using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Media;
using System.IO;

namespace SynthEBD
{
    public class VM_CustomEnvironment : VM
    {
        public VM_CustomEnvironment(Window_CustomEnvironment window, string message, SkyrimRelease initRelease, string initDataFolder)
        {
            InstructionMessage = message;
            OriginalHeight = window.ActualHeight;

            IsValidated = false;
            CustomGamePath = "";
            SkyrimRelease = initRelease;

            if (!string.IsNullOrEmpty(initDataFolder))
            {
                string trialDir = Path.GetDirectoryName(initDataFolder);
                if (trialDir != null)
                {
                    string trialSE = Path.Combine(trialDir, "SkyrimSE.exe");
                    string trialLE = Path.Combine(trialDir, "TESV.exe");
                    string trialVR = Path.Combine(trialDir, "SkyrimVR.exe");

                    if (File.Exists(trialSE)) { CustomGamePath = trialSE; }
                    else if (File.Exists(trialLE)) { CustomGamePath = trialLE; }
                    else if (File.Exists(trialVR)) { CustomGamePath = trialVR; }
                }
            }

            SelectCustomGameFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile("", "Executable files (*.exe)|*.exe", "Select your game executable", out var gamePath) && !string.IsNullOrWhiteSpace(gamePath))
                    {
                        CustomGamePath = gamePath;
                    }
                }
                );

            ClearCustomGameFolder = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    CustomGamePath = String.Empty;
                    CustomGameDataDir = String.Empty;
                }
                );

            this.WhenAnyValue(x => x.CustomGamePath).Subscribe(_ =>
               {
                   var gameDir = Path.GetDirectoryName(CustomGamePath);
                   if (gameDir != null)
                   {
                       CustomGameDataDir = Path.Combine(gameDir, "data");
                   }
               });

            this.WhenAnyValue(x => x.CustomGameDataDir, x => x.SkyrimRelease).Subscribe(_ => UpdateTrialEnvironment());

            DisplayCurrentEnvironmentError = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    CustomMessageBox.DisplayNotificationOK("Environment Creation Error", CurrentError);
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
        public string InstructionMessage { get; set; }
        public string StatusStr { get; set; } = "No game path selected.";
        public SolidColorBrush StatusFontColor { get; set; } = new(Colors.Yellow);
        public string CurrentError { get; set; } = string.Empty;
        public bool ShowErrorExplanationButton { get; set; } = false;
        public string CustomGamePath { get; set; }
        public string CustomGameDataDir { get; set; }
        public bool IsValidated { get; set; }
        public double OriginalHeight { get; set; }
        public SkyrimRelease SkyrimRelease { get; set; }
        public RelayCommand SelectCustomGameFolder { get; set; }
        public RelayCommand ClearCustomGameFolder { get; set; }
        public RelayCommand DisplayCurrentEnvironmentError { get; set; }
        public RelayCommand OK { get; set; }
        public RelayCommand Exit { get; set; }
        public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> TrialEnvironment { get; set; }
        public ObservableCollection<string> LoadOrderMods { get; set; } = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public void UpdateTrialEnvironment()
        {
            ShowErrorExplanationButton = false;
            LoadOrderMods.Clear();
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                IsValidated = false;
                var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimRelease.ToGameRelease());
                if (!String.IsNullOrWhiteSpace(CustomGameDataDir))
                {
                    builder = builder.WithTargetDataFolder(CustomGameDataDir);
                }
                TrialEnvironment = builder
                    .TransformModListings(x => x.OnlyEnabledAndExisting()) // ignore output mod here. This is solely a trial environment to see if the user's settings are valid. Environment provider will then update and add patch mod
                    .Build();
                builder.Build();
                Validate();
            }
            catch (Exception ex)
            {
                var errorString = ExceptionLogger.GetExceptionStack(ex, "");
                CurrentError = "Environment creation failed with error:" + System.Environment.NewLine + errorString;
                if (errorString.StartsWith("Could not locate plugins file"))
                {
                    CurrentError = "Attention: The following error can occur if you normally start your game via a mod manager, but have started SynthEBD outside of your mod manager" + Environment.NewLine + CurrentError;
                }
            }
            Cursor.Current = Cursors.Default;
            
            if (IsValidated)
            {
                StatusStr = "Environment created";
                StatusFontColor = new(Colors.Green);

                if (string.IsNullOrWhiteSpace(CustomGameDataDir))
                {
                    StatusStr += " with default game directory at:" + Environment.NewLine + TrialEnvironment.DataFolderPath;
                }

                LoadOrderMods = new ObservableCollection<string>(TrialEnvironment.LinkCache.ListedOrder.Select(x => x.ModKey.FileName.String));
            }
            else if (string.IsNullOrWhiteSpace(CustomGameDataDir))
            {
                StatusStr = "No game path selected";
                StatusFontColor = new(Colors.Yellow);
            }
            else
            {
                StatusStr = "Invalid environment settings";
                StatusFontColor = new(Colors.Red);
                ShowErrorExplanationButton = true;
            }
        }

        private void Validate()
        {
            if (TrialEnvironment.LinkCache.ListedOrder.Any())
            {
                IsValidated = true;
            }
            else
            {
                CurrentError = "The chosen executable does not appear to be a Skyrim release.";
            }
        }
    }
}
