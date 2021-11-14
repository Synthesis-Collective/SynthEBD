using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Mutagen.Bethesda.WPF;

namespace SynthEBD
{
    public class VM_RunButton : INotifyPropertyChanged
    {
        public VM_RunButton(MainWindow_ViewModel parentWindow)
        {
            this.BackgroundColor = new SolidColorBrush(Colors.Green);
            this.ParentWindow = parentWindow;

            // synchronous version for debugging onlyh
            /*
            ClickRun = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    ParentWindow.DisplayedViewModel = ParentWindow.LogDisplayVM;
                    MainLoop.RunPatcher(
                        ParentWindow.GeneralSettings, ParentWindow.TexMeshSettings, ParentWindow.HeightSettings,
                        ParentWindow.BodyGenSettings, ParentWindow.AssetPacks, ParentWindow.HeightConfigs, ParentWindow.SpecificNPCAssignments,
                        ParentWindow.BlockList, ParentWindow.LinkedNPCNameExclusions, ParentWindow.LinkedNPCGroups, ParentWindow.TrimPaths);
        }
                );
            */

            ClickRun = ReactiveUI.ReactiveCommand.CreateFromTask(
                
                execute: async _ =>
                {
                    ParentWindow.DisplayedViewModel = ParentWindow.LogDisplayVM;

                    await Task.Run(() => MainLoop.RunPatcher(
                        ParentWindow.GeneralSettings, ParentWindow.TexMeshSettings, ParentWindow.HeightSettings,
                        ParentWindow.BodyGenSettings, ParentWindow.AssetPacks, ParentWindow.HeightConfigs, ParentWindow.SpecificNPCAssignments,
                        ParentWindow.BlockList, ParentWindow.LinkedNPCNameExclusions, ParentWindow.LinkedNPCGroups, ParentWindow.TrimPaths));
                });

        }
        public SolidColorBrush BackgroundColor { get; set; }

        public MainWindow_ViewModel ParentWindow { get; set; }

        public ReactiveUI.IReactiveCommand ClickRun { get; }
        //public SynthEBD.RelayCommand ClickRun { get; }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
