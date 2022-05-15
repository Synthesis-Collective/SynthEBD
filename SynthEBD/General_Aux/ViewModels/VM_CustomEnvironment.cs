using BespokeFusion;
using Mutagen.Bethesda;
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
                            var builder = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(SkyrimRelease.ToGameRelease());
                            if (!String.IsNullOrWhiteSpace(gameDir))
                            {
                                builder = builder.WithTargetDataFolder(gameDir);
                            }
                            Environment = builder
                                .TransformModListings(x =>
                                    x.OnlyEnabledAndExisting().
                                    RemoveModAndDependents(PatcherEnvironmentProvider.Instance.PatchFileName, verbose: true))
                                    .WithOutputMod(PatcherEnvironmentProvider.Instance.OutputMod)
                                .Build();
                            builder.Build();
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
        public string CustomGamePath { get; set; }
        public bool IsValidated { get; set; }
        public SkyrimRelease SkyrimRelease { get; set; }
        public RelayCommand SelectCustomGameFolder { get; set; }
        public RelayCommand OK { get; set; }
        public RelayCommand Exit { get; set; }
        public IGameEnvironment<ISkyrimMod, ISkyrimModGetter> Environment { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Validate()
        {
            if (Environment.LinkCache.ListedOrder.Count > 1)
            {
                IsValidated = true;
            }
            else
            {
                CustomMessageBox.DisplayNotificationOK("Invalid Environment", "The chosen executable does not appear to be a Skyrim release.");
            }
        }
    }
}
