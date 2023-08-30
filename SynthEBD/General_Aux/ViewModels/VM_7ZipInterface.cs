using Alphaleonis.Win32.Security;
using Noggog;
using Noggog.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_7ZipInterface : VM
    {
        private readonly _7ZipInterface _7z;
        public VM_7ZipInterface(_7ZipInterface sevenZ)
        {
            _7z = sevenZ;

            consoleUpdates
               .ObserveOnGui()
               .Subscribe(i =>
               {
                   PutThisOnScreen += (i);
               })
               .DisposeWith(this);

            AddToScreen = (string s) =>
            {
                consoleUpdates.OnNext(Environment.NewLine + s);
            };
        }

        public string PutThisOnScreen { get; set; } = string.Empty;
        Subject<string> consoleUpdates { get; set; } = new();
        public Action<string> AddToScreen { get; }
        public RelayCommand StartExtraction { get; }
        private Window_7ZipInterface _window { get; set; }

        private void DisplayWindow()
        {
            _window = new Window_7ZipInterface();
            _window.DataContext = this;
            _window.Show();
        }

        public bool ExtractArchive(string sourcePath, string destinationPath, bool showWindow, bool closeWindowWhenDone)
        {
            if (showWindow)
            {
                DisplayWindow();
            }

            var result = Task.Run(() => _7z.ExtractArchiveNew(sourcePath, destinationPath, false, AddToScreen)).Result;
            
            if (closeWindowWhenDone && _window != null)
            {
                _window.Close();
            }

            return result;
        }

        public List<string> GetArchiveContents(string archivePath)
        {
            return _7z.GetArchiveContents(archivePath);
        }
    }
}
