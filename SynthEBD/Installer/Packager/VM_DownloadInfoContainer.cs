using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    /// <summary>
    /// Packager-side editor row for one external dependency archive entry under a <see cref="VM_PackagerOption"/>.
    /// Round-trips to/from <see cref="Manifest.DownloadInfoContainer"/> and supports self-removal from its parent.
    /// </summary>
    public class VM_DownloadInfoContainer : VM
    {
        /// <summary>Stores the owning option and wires the Delete command (removes this row from the parent).</summary>
        public VM_DownloadInfoContainer(VM_PackagerOption parent)
        {
            Parent = parent;
            DeleteCommand = new RelayCommand(canExecute: _ => true, execute: _ => Parent.DownloadInfo.Remove(this));
        }
        public string ModPageName { get; set; } = "";
        public string ModDownloadName { get; set; }
        public string URL { get; set; } = "";
        public string ExpectedFileName { get; set; } = "";
        public string ExtractionSubPath { get; set; } = "";
        public VM_PackagerOption Parent { get; set; }
        public RelayCommand DeleteCommand { get; set; }

        /// <summary>Builds an editor row VM from a persisted <see cref="Manifest.DownloadInfoContainer"/>.</summary>
        public static VM_DownloadInfoContainer GetViewModelFromModel(Manifest.DownloadInfoContainer model, VM_PackagerOption parent)
        {
            var viewModel = new VM_DownloadInfoContainer(parent);
            viewModel.ModPageName = model.ModPageName;
            viewModel.ModDownloadName = model.ModDownloadName;
            viewModel.URL = model.URL;
            viewModel.ExpectedFileName = model.ExpectedFileName;
            viewModel.ExtractionSubPath = model.ExtractionSubPath;
            return viewModel;
        }
        /// <summary>Serializes this editor row back into a <see cref="Manifest.DownloadInfoContainer"/> model.</summary>
        public Manifest.DownloadInfoContainer DumpViewModelToModel()
        {
            Manifest.DownloadInfoContainer model = new();
            model.ModPageName = ModPageName;
            model.ModDownloadName = ModDownloadName;
            model.URL = URL;
            model.ExpectedFileName = ExpectedFileName;
            model.ExtractionSubPath = ExtractionSubPath;
            return model;
        }
    }
}
