using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Mutagen.Bethesda.Skyrim;
using System.Windows;
using System.ComponentModel;

namespace SynthEBD
{
    public class VM_FilePathReplacement : INotifyPropertyChanged
    {
        public VM_FilePathReplacement(VM_FilePathReplacementMenu parentMenu)
        {
            this.Source = "";
            this.Destination = "";

            this.SourceBorderColor = new SolidColorBrush(Colors.Red);
            this.DestBorderColor = new SolidColorBrush(Colors.Red);

            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => parentMenu.Paths.Remove(this));
            FindPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog();
                    if (Source != "")
                    {
                        var initDir = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, Path.GetDirectoryName(Source));
                        if (Directory.Exists(initDir))
                        {
                            dialog.InitialDirectory = initDir;
                        }
                    }
                    if (dialog.ShowDialog() == true) 
                    { 
                        // try to figure out the root directory
                        if (dialog.FileName.Contains(GameEnvironmentProvider.MyEnvironment.DataFolderPath))
                        {
                            Source = dialog.FileName.Replace(GameEnvironmentProvider.MyEnvironment.DataFolderPath, "").TrimStart(Path.DirectorySeparatorChar);
                        }
                        else if (TrimKnownPrefix(dialog.FileName, out var sourceTrimmed))
                        {
                            Source = sourceTrimmed;
                        }
                        else if (dialog.FileName.Contains("Data", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var index = dialog.FileName.IndexOf("Data", 0, StringComparison.InvariantCultureIgnoreCase);
                            Source = dialog.FileName.Remove(0, index + 4).TrimStart(Path.DirectorySeparatorChar);
                        }
                        else
                        {
                            MessageBox.Show("Cannot figure out where the Data folder is within the supplied path. You will need to edit the path so that it starts one folder beneath the Data folder.");
                            Source = dialog.FileName;
                        }
                    }
                }
                );


            ParentMenu = parentMenu;

            this.WhenAnyValue(x => x.Source).Subscribe(x => RefreshSourceColor());
            this.WhenAnyValue(x => x.Destination).Subscribe(x => RefreshDestColor());
            ParentMenu.WhenAnyValue(x => x.ReferenceNPC).Subscribe(x => RefreshDestColor());
        }

        public string Source { get; set; }
        public string Destination { get; set; }

        public SolidColorBrush SourceBorderColor { get; set; }
        public SolidColorBrush DestBorderColor { get; set; }

        public RelayCommand DeleteCommand { get; }
        public RelayCommand FindPath { get; }

        public VM_FilePathReplacementMenu ParentMenu { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public static VM_FilePathReplacement GetViewModelFromModel(FilePathReplacement model, VM_FilePathReplacementMenu parentMenu)
        {
            VM_FilePathReplacement viewModel = new VM_FilePathReplacement(parentMenu);
            viewModel.Source = model.Source;
            viewModel.Destination = model.Destination;

            return viewModel;
        }

        public void RefreshSourceColor()
        {
            var searchStr = Path.Combine(GameEnvironmentProvider.MyEnvironment.DataFolderPath, this.Source);
            if (File.Exists(searchStr))
            {
                this.SourceBorderColor = new SolidColorBrush(Colors.LightGreen);
            }
            else
            {
                this.SourceBorderColor = new SolidColorBrush(Colors.Red);
            }
        }

        public void RefreshDestColor()
        {
            if(RecordPathParser.GetObjectAtPath(ParentMenu.ReferenceNPC, this.Destination, new Dictionary<dynamic, Dictionary<string, dynamic>>(), ParentMenu.ReferenceLinkCache, out var objAtPath) && objAtPath != null && objAtPath.GetType() == typeof(string))
            {
                this.DestBorderColor = new SolidColorBrush(Colors.LightGreen);
            }
            else
            {
                this.DestBorderColor = new SolidColorBrush(Colors.Red);
            }
        }

        private static bool TrimKnownPrefix(string s, out string trimmed)
        {
            trimmed = "";
            foreach (var trim in PatcherSettings.TexMesh.TrimPaths)
            {
                if (s.Contains(trim.PathToTrim) && s.EndsWith(trim.Extension))
                {
                    trimmed = s.Remove(0, s.IndexOf(trim.PathToTrim)).TrimStart(Path.DirectorySeparatorChar);
                    return true;
                }
            }
            return false;
        }
    }
}
