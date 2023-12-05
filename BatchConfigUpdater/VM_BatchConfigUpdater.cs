using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using SynthEBD;

namespace BatchConfigUpdater
{
    public class VM_BatchConfigUpdater
    {
        public VM_BatchConfigUpdater()
        {
            SelectFolder = new RelayCommand(
            canExecute: _ => true,
            execute: _ =>
            {
                if (IO_Aux.SelectFolder("", out string selectedPath))
                {
                    List<string> errors = new();
                    UpdateJsons(selectedPath, errors);

                    if (errors.Any())
                    {
                        MessageWindow.DisplayNotificationOK("Errors", String.Join(Environment.NewLine, errors));
                    }
                }
            }
        );
        }

        public RelayCommand SelectFolder { get; }

        public void UpdateJsons(string rootFolder, List<string> errors)
        {
            var jsonPaths = Directory.GetFiles(rootFolder);
            foreach (var path in jsonPaths.Where(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                var ap = JSONhandler<AssetPack>.LoadJSONFile(path, out bool success, out string exception);
                if (success && !string.IsNullOrEmpty(ap.GroupName))
                {
                    foreach (var subgroup in ap.Subgroups)
                    {
                        UpdatePaths(subgroup);
                    }
                    foreach (var replacer in ap.ReplacerGroups)
                    {
                        foreach (var subgroup in replacer.Subgroups)
                        {
                            UpdatePaths(subgroup);
                        }
                    }
                    JSONhandler<AssetPack>.SaveJSONFile(ap, path, out bool saveSuccess, out string saveException);
                    if (!saveSuccess)
                    {
                        errors.Add("Save error: " + path);
                    }
                    else
                    {
                        errors.Add("Save successully: " + path);
                    }
                }
                else
                {
                    errors.Add("Load error: " + path);
                }
            }

            foreach (var dirPath in Directory.GetDirectories(rootFolder))
            {
                UpdateJsons(dirPath, errors);
            }
        }

        public static void UpdatePaths(AssetPack.Subgroup sg)
        {
            foreach (var path in sg.Paths)
            {
                if (path.Destination.EndsWith(".Diffuse") || path.Destination.EndsWith(".NormalOrGloss") || path.Destination.EndsWith(".GlowOrDetailMap") || path.Destination.EndsWith(".BacklightMaskOrSpecular") || path.Destination.EndsWith(".Height"))
                {
                    path.Destination += ".RawPath";
                }
            }

            foreach (var sugroup in sg.Subgroups)
            {
                UpdatePaths(sugroup);
            }
        }
    }
}
