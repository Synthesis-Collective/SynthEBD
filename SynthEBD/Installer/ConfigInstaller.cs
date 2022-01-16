using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using SharpCompress;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace SynthEBD
{
    public class ConfigInstaller
    {
        public static void InstallConfigFile(string path)
        {
            string outputFolder = Path.Combine(PatcherSettings.ModManagerIntegration.TempExtractionFolder, DateTime.Now.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outputFolder);

            try
            {
                if (!ExtractConfigArchive(path, outputFolder))
                {
                    return;
                }
            }
            catch
            {
                MessageBox.Show("Archive extraction failed. This may be because the resulting file paths were too long. Try moving your Temp Folder in Mod Manager Integration to a short path such as your desktop. Installation aborted.");
            }

            string manifestPath = Path.Combine(outputFolder, "Manifest.json");
            if (!File.Exists(manifestPath))
            {
                MessageBox.Show("Could not find Manifest.json in " + outputFolder + ". Installation aborted.");
            }

            Manifest manifest = null;
            try
            {
                manifest = JSONhandler<Manifest>.loadJSONFile(manifestPath);
                if (manifest == null)
                {
                    MessageBox.Show("Could not parse Manifest.json in " + outputFolder + ". Installation aborted.");
                    return;
                }
            }
            catch
            {
                MessageBox.Show("Could not parse Manifest.json in " + outputFolder + ". Installation aborted.");
                return;
            }

            var installerWindow = new Window_ConfigInstaller();
            var installerVM = new VM_ConfigInstaller(manifest, installerWindow);
            installerWindow.DataContext = installerVM;
            //installerVM.SelectorMenu = new VM_ConfigSelector(manifest, installerWindow);
            //installerVM.SelectorMenu.SelectedOption = installerVM.SelectorMenu;
            //installerVM.DisplayedViewModel = installerVM.SelectorMenu;
            installerWindow.ShowDialog();

            foreach (var bgPath in installerVM.SelectorMenu.SelectedBodyGenConfigPaths)
            {
                File.Move(Path.Combine(outputFolder, bgPath), Path.Combine(PatcherSettings.Paths.BodyGenConfigDirPath, bgPath), false);
            }

            foreach (var templatePath in installerVM.SelectorMenu.SelectedRecordTemplatePaths)
            {
                File.Move(Path.Combine(outputFolder, templatePath), Path.Combine(PatcherSettings.Paths.RecordTemplatesDirPath, templatePath), false);
            }

            foreach (var assetPath in installerVM.SelectorMenu.SelectedAssetPackPaths)
            {
                File.Move(Path.Combine(outputFolder, assetPath), Path.Combine(PatcherSettings.Paths.AssetPackDirPath, assetPath), false);
            }

            Directory.Delete(outputFolder, true);
        }

        private static bool ExtractConfigArchive(string archivePath, string destinationPath)
        {
            FileInfo archiveInfo = new FileInfo(archivePath);
            if (SevenZipArchive.IsSevenZipFile(archiveInfo))
            {
                var zArchive = SevenZipArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = zArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions();
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                return true;
            }
            else if (RarArchive.IsRarFile(archivePath))
            {
                var rArchive = RarArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = rArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions();
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                return true;
            }
            else if (ZipArchive.IsZipFile(archivePath))
            {
                var ziArchive = ZipArchive.Open(archiveInfo, new ReaderOptions());
                using (var reader = ziArchive.ExtractAllEntries())
                {
                    var options = new ExtractionOptions();
                    options.ExtractFullPath = true;
                    reader.WriteAllToDirectory(destinationPath, options);
                }
                return true;
            }
            else
            {
                MessageBox.Show("Could not extract the config archive. Valid formats are .7z, .zip, and .rar.");
                return false;
            }
        }
    }
}
