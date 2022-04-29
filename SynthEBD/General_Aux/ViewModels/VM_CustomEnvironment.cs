using BespokeFusion;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;

namespace SynthEBD
{
    public class VM_CustomEnvironment : INotifyPropertyChanged
    {
        public VM_CustomEnvironment(Window_CustomEnvironment window)
        {
            IsValidated = false;
            CustomGamePath = "";
            SkyrimRelease = SkyrimRelease.SkyrimSE;

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
                            FailureMessage("Environment creation failed with error:" + System.Environment.NewLine + ExceptionLogger.GetExceptionStack(ex, ""));
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
        public string CustomGamePath { get; set; }
        public bool IsValidated { get; set; }
        public SkyrimRelease SkyrimRelease { get; set; }
        public RelayCommand SelectCustomGameFolder { get; set; }
        public RelayCommand OK { get; set; }
        public RelayCommand Exit { get; set; }
        public IGameEnvironmentState<ISkyrimMod, ISkyrimModGetter> Environment { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

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
                FailureMessage("The chosen executable does not appear to be a Skyrim release.");
            }
        }

        private void FailureMessage(string message)
        {
            var box = new CustomMaterialMessageBox()
            {
                TxtMessage = { Text = message, Foreground = Brushes.White },
                TxtTitle = { Text = "Invalid Environment", Foreground = Brushes.White },
                BtnOk = { Content = "Ok" },
                BtnCancel = { Visibility = Visibility.Hidden },
                MainContentControl = { Background = Brushes.Black },
                TitleBackgroundPanel = { Background = Brushes.Black },
                BorderBrush = Brushes.Silver
            };
            box.Show();
        }
    }
}
