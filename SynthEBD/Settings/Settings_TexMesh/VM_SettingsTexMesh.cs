using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD
{
    public class VM_SettingsTexMesh : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public VM_SettingsTexMesh(MainWindow_ViewModel mainViewModel)
        {
            this.bChangeNPCTextures = true;
            this.bChangeNPCMeshes = true;
            this.bApplyToNPCsWithCustomSkins = true;
            this.bApplyToNPCsWithCustomFaces = true;
            this.bForceVanillaBodyMeshPath = false;
            this.bDisplayPopupAlerts = true;
            this.bGenerateAssignmentLog = true;
            this.TrimPaths = new ObservableCollection<TrimPath>();
            this.AssetPacks = new ObservableCollection<VM_AssetPack>();

            ParentViewModel = mainViewModel;

            AddTrimPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.TrimPaths.Add(new TrimPath())
                );
            RemoveTrimPath = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: x => this.TrimPaths.Remove((TrimPath)x)
                );
            ValidateAll = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (ValidateAllConfigs(mainViewModel.BodyGenConfigs, out List<string> errors))
                    {
                        MessageBox.Show("No errors found.");
                    }
                    else
                    {
                        Logger.LogMessage(String.Join(Environment.NewLine, errors));
                        mainViewModel.DisplayedViewModel = mainViewModel.LogDisplayVM;
                    }
                }
                );
            AddNewAssetPackConfigFile = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ => this.AssetPacks.Add(new VM_AssetPack(this.AssetPacks, ParentViewModel.BGVM, ParentViewModel.OBVM.DescriptorUI, ParentViewModel.SGVM, ParentViewModel.RecordTemplateLinkCache, ParentViewModel))
                );

            InstallFromArchive = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    ParentViewModel.MMVM.UpdatePatcherSettings(); // make sure mod manager integration is synced w/ latest settings
                    var installedConfigs = ConfigInstaller.InstallConfigFile();
                    mainViewModel.SaveAndRefreshPlugins();
                    foreach (var newConfig in mainViewModel.TMVM.AssetPacks.Where(x => installedConfigs.Contains(x.GroupName)))
                    {
                        newConfig.IsSelected = true;
                    }
                }
                );

            InstallFromJson = new SynthEBD.RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    if (IO_Aux.SelectFile(PatcherSettings.Paths.AssetPackDirPath, "Config files (*.json)|*.json", out string path))
                    {
                        var newAssetPack = SettingsIO_AssetPack.LoadAssetPack(path, PatcherSettings.General.RaceGroupings, ParentViewModel.RecordTemplatePlugins, ParentViewModel.BodyGenConfigs);
                        if (newAssetPack != null)
                        {
                            var newAssetPackVM = VM_AssetPack.GetViewModelFromModel(newAssetPack, ParentViewModel.SGVM, AssetPacks, ParentViewModel.BGVM, ParentViewModel.OBVM.DescriptorUI, ParentViewModel.RecordTemplateLinkCache, ParentViewModel);
                            newAssetPackVM.IsSelected = true;
                            AssetPacks.Add(newAssetPackVM);
                        }
                    }
                }
                );
        }

        public bool bChangeNPCTextures { get; set; }
        public bool bChangeNPCMeshes { get; set; }
        public bool bApplyToNPCsWithCustomSkins { get; set; }
        public bool bApplyToNPCsWithCustomFaces { get; set; }
        public bool bForceVanillaBodyMeshPath { get; set; }
        public bool bDisplayPopupAlerts { get; set; }
        public bool bGenerateAssignmentLog { get; set; }

        public ObservableCollection<TrimPath> TrimPaths { get; set; }

        public ObservableCollection<VM_AssetPack> AssetPacks { get; set; }

        public MainWindow_ViewModel ParentViewModel { get; set; }

        public RelayCommand AddTrimPath { get; }
        public RelayCommand RemoveTrimPath { get; }
        public RelayCommand ValidateAll { get; }
        public RelayCommand AddNewAssetPackConfigFile { get; }
        public RelayCommand InstallFromArchive { get; }
        public RelayCommand InstallFromJson { get; }

        public bool ValidateAllConfigs(BodyGenConfigs bodyGenConfigs, out List<string> errors)
        {
            bool isValid = true;
            errors = new List<string>();
            foreach (var config in AssetPacks)
            {
                if (!config.Validate(bodyGenConfigs, out var configErrors))
                {
                    isValid = false;
                    errors.AddRange(configErrors);
                    errors.Add("");
                }
            }
            return isValid;
        }

        public static void GetViewModelFromModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            viewModel.bChangeNPCTextures = model.bChangeNPCTextures;
            viewModel.bChangeNPCMeshes = model.bChangeNPCMeshes;
            viewModel.bApplyToNPCsWithCustomSkins = model.bApplyToNPCsWithCustomSkins;
            viewModel.bApplyToNPCsWithCustomFaces = model.bApplyToNPCsWithCustomFaces;
            viewModel.bForceVanillaBodyMeshPath = model.bForceVanillaBodyMeshPath;
            viewModel.bDisplayPopupAlerts = model.bDisplayPopupAlerts;
            viewModel.bGenerateAssignmentLog = model.bGenerateAssignmentLog;
            viewModel.TrimPaths = new ObservableCollection<TrimPath>(model.TrimPaths);
        }

        public static void DumpViewModelToModel(VM_SettingsTexMesh viewModel, Settings_TexMesh model)
        {
            model.bChangeNPCTextures = viewModel.bChangeNPCTextures;
            model.bChangeNPCMeshes = viewModel.bChangeNPCMeshes;
            model.bApplyToNPCsWithCustomSkins = viewModel.bApplyToNPCsWithCustomSkins;
            model.bApplyToNPCsWithCustomFaces = viewModel.bApplyToNPCsWithCustomFaces;
            model.bForceVanillaBodyMeshPath = viewModel.bForceVanillaBodyMeshPath;
            model.bDisplayPopupAlerts = viewModel.bDisplayPopupAlerts;
            model.bGenerateAssignmentLog = viewModel.bGenerateAssignmentLog;
            model.SelectedAssetPacks = viewModel.AssetPacks.Where(x => x.IsSelected).Select(x => x.GroupName).ToHashSet();
        }

    }
}
