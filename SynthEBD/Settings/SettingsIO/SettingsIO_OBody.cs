using System.IO;

namespace SynthEBD;

public class SettingsIO_OBody
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherState _patcherState;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_OBody(IEnvironmentStateProvider environmentProvider, PatcherState patcherState, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _patcherState = patcherState;
        _logger = logger;
        _paths = paths;
    }
    public Settings_OBody LoadOBodySettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading OBody settings from disk");
        Settings_OBody oBodySettings = new();

        loadSuccess = true;

        if (File.Exists(_paths.OBodySettingsPath))
        {
            oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(_paths.OBodySettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load O/AutoBody Settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.OBodySettingsPath)))
        {
            oBodySettings = JSONhandler<Settings_OBody>.LoadJSONFile(_paths.GetFallBackPath(_paths.OBodySettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load O/AutoBody Settings. Error: " + exceptionStr);
            }
        }

        if (oBodySettings == null)
        {
            oBodySettings = new();
        }

        foreach (var attributeGroup in _patcherState.GeneralSettings.AttributeGroups) // add any available attribute groups from the general patcher settings
        {
            if (!oBodySettings.AttributeGroups.Select(x => x.Label).Contains(attributeGroup.Label))
            {
                oBodySettings.AttributeGroups.Add(new AttributeGroup() { Label = attributeGroup.Label, Attributes = new HashSet<NPCAttribute>(attributeGroup.Attributes) });
            }
        }
        _logger.LogStartupEventEnd("Loading OBody settings from disk");
        return oBodySettings;
    }

    public Dictionary<string, HashSet<string>> LoadDefaultBodySlideAnnotation()
    {
        Dictionary<string, HashSet<string>> output = new Dictionary<string, HashSet<string>>();

        string annotationDir = Path.Combine(_environmentProvider.InternalDataPath, "Default BodySlide Annotations");

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