using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

public class SettingsIO_HeadParts
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    public SettingsIO_HeadParts(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
    public Settings_Headparts LoadHeadPartSettings(out bool loadSuccess)
    {
        _logger.LogStartupEventStart("Loading HeadPart settings from disk");
        Settings_Headparts headPartSettings = new Settings_Headparts();

        loadSuccess = true;

        if (File.Exists(_paths.HeadPartsSettingsPath))
        {
            headPartSettings = JSONhandler<Settings_Headparts>.LoadJSONFile(_paths.HeadPartsSettingsPath, out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load head part settings. Error: " + exceptionStr);
            }
        }
        else if (File.Exists(_paths.GetFallBackPath(_paths.HeadPartsSettingsPath)))
        {
            headPartSettings = JSONhandler<Settings_Headparts>.LoadJSONFile(_paths.GetFallBackPath(_paths.HeadPartsSettingsPath), out loadSuccess, out string exceptionStr);
            if (!loadSuccess)
            {
                _logger.LogError("Could not load head part settings. Error: " + exceptionStr);
            }
        }

        if (headPartSettings == null)
        {
            headPartSettings = new Settings_Headparts();
        }
        _logger.LogStartupEventEnd("Loading HeadPart settings from disk");
        return headPartSettings;
    }
}
