using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class VM_DownloadInfoContainer : VM
    {
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
