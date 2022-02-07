using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class SettingsIO_OBody
    {
        public static Settings_OBody LoadOBodySettings()
        {
            Settings_OBody oBodySettings = new Settings_OBody();

            if (File.Exists(PatcherSettings.Paths.OBodySettingsPath))
            {
                oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(PatcherSettings.Paths.OBodySettingsPath);
            }
            else if (File.Exists(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.OBodySettingsPath)))
            {
                oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(PatcherSettings.Paths.GetFallBackPath(PatcherSettings.Paths.OBodySettingsPath));
            }

            foreach (var attributeGroup in PatcherSettings.General.AttributeGroups) // add any available attribute groups from the general patcher settings
            {
                if (!oBodySettings.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
                {
                    oBodySettings.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
                }
            }

            return oBodySettings;
        }

        public static Dictionary<string, HashSet<string>> LoadDefaultBodySlideAnnotation()
        {
            Dictionary<string, HashSet<string>> output = new Dictionary<string, HashSet<string>>();

            string annotationDir = Path.Combine(PatcherSettings.Paths.ResourcesFolderPath, "Default BodySlide Annotations");

            if (Directory.Exists(annotationDir))
            {
                var filePaths = Directory.GetFiles(annotationDir, "*.csv").OrderBy(f => f); // sort alphabetical https://stackoverflow.com/questions/6294275/sorting-the-result-of-directory-getfiles-in-c-sharp

                foreach (string path in filePaths)
                {
                    string[] lines = System.IO.File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        string[] columns = line.Split(',');
                        
                        if (columns.Length > 1)
                        {
                            string configName = columns[0].Trim();
                            HashSet<string> descriptors = new HashSet<string>();

                            for (int i = 1; i < columns.Length; i++ )
                            {
                                string descriptor = columns[i].Trim();
                                if (descriptor.Any())
                                {
                                    descriptors.Add(columns[i].Trim());
                                }
                            }

                            if (output.ContainsKey(configName))
                            {
                                output[configName] = descriptors;
                            }
                            else
                            {
                                output.Add(configName, descriptors);
                            }
                        }
                    }
                }
            }

            return output;
        }
    }
}
