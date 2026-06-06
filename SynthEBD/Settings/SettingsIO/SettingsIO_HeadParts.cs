using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD;

/// <summary>
/// Loads the head-part settings (<see cref="Settings_Headparts"/>) from JSON, with primary/fallback path
/// resolution and a default-object fallback when no file is present.
/// </summary>
public class SettingsIO_HeadParts
{
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Injects the logger and path resolver.</summary>
    public SettingsIO_HeadParts(Logger logger, SynthEBDPaths paths)
    {
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Loads the head-part settings from the primary path, then the fallback path, returning a fresh default
    /// if neither exists or if parsing yields null. Reads from disk.
    /// </summary>
    /// <param name="loadSuccess">Set true if loaded cleanly (or defaulted), false on parse error.</param>
    /// <returns>The loaded or default <see cref="Settings_Headparts"/>.</returns>
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
