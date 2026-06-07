using Alphaleonis.Win32.Security;
using Noggog;
using Noggog.WPF;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// View model that wraps <see cref="_7ZipInterface"/> to surface 7-Zip extraction/listing progress in a
    /// console-style window. Console lines pushed via <see cref="AddToScreen"/> are buffered and marshalled
    /// onto the UI thread before being appended to <see cref="PutThisOnScreen"/>.
    /// </summary>
    public class VM_7ZipInterface : VM
    {
        /// <summary>Autofac factory delegate for constructing a <see cref="VM_7ZipInterface"/>.</summary>
        public delegate VM_7ZipInterface Factory();
        private readonly _7ZipInterface _7z;
        /// <summary>Subscribes the buffered console-update stream (100&#160;ms / 100-line batches, on the UI thread) to <see cref="PutThisOnScreen"/>, and wires <see cref="AddToScreen"/> to push lines into that stream.</summary>
        public VM_7ZipInterface(_7ZipInterface sevenZ)
        {
            _7z = sevenZ;

            consoleUpdates
               .ObserveOnGui()
               .Buffer(TimeSpan.FromMilliseconds(100), 100)
               .Where(list => list.Count > 0)
               .Subscribe(i =>
               {
                   //PutThisOnScreen += (i);
                   PutThisOnScreen += string.Join(Environment.NewLine, i);
               })
               .DisposeWith(this);

            AddToScreen = (string s) =>
            {
                consoleUpdates.OnNext(Environment.NewLine + s);
            };
        }

        /// <summary>The accumulated console text bound to the progress window.</summary>
        public string PutThisOnScreen { get; set; } = string.Empty;
        /// <summary>Hot stream of console lines, buffered and flushed to <see cref="PutThisOnScreen"/> on the UI thread.</summary>
        Subject<string> consoleUpdates { get; set; } = new();
        /// <summary>Callback handed to <see cref="_7ZipInterface"/> to append a line of console output (pushes into <see cref="consoleUpdates"/>).</summary>
        public Action<string> AddToScreen { get; }
        private Window_7ZipInterface _window { get; set; }

        /// <summary>Creates the progress window, binds it to this VM, and shows it.</summary>
        private void DisplayWindow()
        {
            _window = new Window_7ZipInterface();
            _window.DataContext = this;
            _window.Show();
        }

        /// <summary>Extracts an archive on a background thread, optionally showing the progress window and auto-closing it (after a delay) when done.</summary>
        /// <param name="archivePath">Path to the archive to extract.</param>
        /// <param name="destinationPath">Directory to extract into.</param>
        /// <param name="showWindow">Whether to show the progress window.</param>
        /// <param name="closeWindowWhenDone">Whether to close the window after extraction.</param>
        /// <param name="pauseMilliseconds">Delay before auto-closing the window.</param>
        /// <returns><c>true</c> if extraction succeeded.</returns>
        public async Task<bool> ExtractArchive(string archivePath, string destinationPath, bool showWindow, bool closeWindowWhenDone, int pauseMilliseconds)
        {
            PutThisOnScreen = "";

            if (showWindow)
            {
                DisplayWindow();
            }

            var result = await Task.Run(() => _7z.ExtractArchive(archivePath, destinationPath, true, AddToScreen));

            if (closeWindowWhenDone && _window != null)
            {
                await Task.Delay(pauseMilliseconds);
                _window.Close();
            }

            return result;
        }

        /// <summary>Lists an archive's contents on a background thread, optionally showing the progress window and auto-closing it (after a delay) when done.</summary>
        /// <param name="archivePath">Path to the archive to inspect.</param>
        /// <param name="showWindow">Whether to show the progress window.</param>
        /// <param name="closeWindowWhenDone">Whether to close the window when finished.</param>
        /// <param name="pauseMilliseconds">Delay before auto-closing the window.</param>
        /// <returns>The archive's entry paths.</returns>
        public async Task<List<string>> GetArchiveContents(string archivePath, bool showWindow, bool closeWindowWhenDone, int pauseMilliseconds)
        {
            PutThisOnScreen = "";

            if (showWindow)
            {
                DisplayWindow();
            }

            var result = await Task.Run(() => _7z.GetArchiveContents(archivePath, true, AddToScreen));

            if (closeWindowWhenDone && _window != null)
            {
                await Task.Delay(pauseMilliseconds);
                _window.Close();
            }

            return result;
        }
    }
}
