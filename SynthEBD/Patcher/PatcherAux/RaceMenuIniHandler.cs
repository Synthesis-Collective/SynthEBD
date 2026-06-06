using System.IO;

namespace SynthEBD;

/// <summary>
/// Reads and rewrites the RaceMenu SKEE ini (skee64.ini or skeevr.ini) under the game's Data folder, toggling the
/// BodyMorph/BodyGen settings and scale mode so the runtime is configured correctly for BodyGen or BodySlide output.
/// Used as a supporting step when emitting body-shape data.
/// </summary>
public class RaceMenuIniHandler
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly Logger _logger;
    private readonly SynthEBDPaths _paths;
    /// <summary>Creates the handler with the environment (for the Data folder), logger, and paths.</summary>
    public RaceMenuIniHandler(IEnvironmentStateProvider environmentProvider, Logger logger, SynthEBDPaths paths)
    {
        _environmentProvider = environmentProvider;
        _logger = logger;
        _paths = paths;
    }
    /// <summary>
    /// Reads the RaceMenu SKEE ini into a list of lines, preferring the VR ini (skeevr.ini) when present, else skee64.ini.
    /// <paramref name="success"/> is true only when lines were read; <paramref name="fileName"/> returns the file used.
    /// Returns an empty list (and logs) if the file cannot be accessed.
    /// </summary>
    public List<string> GetRaceMenuIniContents(out bool success, out string fileName)
    {
        success = false;
        string iniPath64 = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
        string iniPathVR = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", "skeevr.ini");

        string iniPath = "";
        if (File.Exists(iniPathVR)) { iniPath = iniPathVR; fileName = "skeevr.ini"; }
        else { iniPath = iniPath64; fileName = "skee64.ini"; }

        List<string> iniContents = new List<string>();
        try
        {
            foreach (string line in System.IO.File.ReadLines(iniPath))
            {
                iniContents.Add(line);
            }
            success = iniContents.Any();
        }
        catch
        {
            _logger.LogMessage("Couldn't access RaceMenu ini at " + iniPath);
        }
        return iniContents;
    }

    /// <summary>
    /// Returns the first ini line starting with <paramref name="settingName"/>, setting <paramref name="success"/> true if
    /// found. Returns "" (and false) when no matching line exists. Match is a prefix <c>StartsWith</c>, not a key parse.
    /// </summary>
    private static string GetIniLine(List<string> iniContents, string settingName, out bool success)
    {
        string relevantLine = iniContents.Where(x => x.StartsWith(settingName)).FirstOrDefault();
        if (relevantLine == null)
        {
            success=false;
            return "";
        }
        else
        {
            success = true;
            return relevantLine;
        }
    }

    /// <summary>
    /// Extracts the trimmed value from a "key=value ; comment" ini line by stripping the comment then splitting on '='.
    /// Sets <paramref name="parsed"/> false (and returns "") when the line does not split into exactly two '='-parts.
    /// </summary>
    private static string GetIniSettingValue(string iniLine, out bool parsed)
    {
        string[] commentSplit = iniLine.Split(';');
        string[] valueSplit = commentSplit[0].Trim().Split('=');
        if (valueSplit.Length != 2)
        {
            parsed = false;
            return "";
        }
        else
        {
            parsed = true;
            return valueSplit[1].Trim();
        }
    }

    /// <summary>
    /// Replaces the value of <paramref name="iniLine"/> in <paramref name="fullIniLines"/> with <paramref name="value"/>,
    /// preserving any trailing comment. Sets <paramref name="parsed"/> false and makes no change when the line does not
    /// split into exactly two '='-parts. Side effect: mutates <paramref name="fullIniLines"/> in place.
    /// </summary>
    private static void SetIniSettingValue(string iniLine, string value, List<string> fullIniLines, out bool parsed)
    {
        string[] commentSplit = iniLine.Split(';');
        string[] valueSplit = commentSplit[0].Trim().Split('=');
        if (valueSplit.Length != 2)
        {
            parsed = false;
            return;
        }
        else
        {
            parsed = true;
            valueSplit[1] = value;
        }

        string comment = "";
        if (commentSplit.Length > 1)
        {
            int pos = iniLine.IndexOf(';');
            comment = iniLine.Substring(pos);
        }

        string newIniLine = valueSplit[0] + "=" + valueSplit[1] + comment;

        fullIniLines[fullIniLines.IndexOf(iniLine)] = newIniLine;
    }

    /// <summary>
    /// Reads the <c>bEnableBodyMorph</c> setting. Returns its boolean value; <paramref name="success"/> is true only when
    /// the line was found and parsed to "0"/"1", and <paramref name="lineInIni"/> returns the raw matched line.
    /// </summary>
    public bool GetBodyMorphEnabled(List<string> iniContents, out bool success, out string lineInIni)
    {
        success=false;
        lineInIni = GetIniLine(iniContents, "bEnableBodyMorph", out bool lineFound);
        if (lineFound)
        {
            string value = GetIniSettingValue(lineInIni, out bool parsed);
            if (parsed)
            {
                success = true;
                switch (value)
                {
                    case "0": return false;
                    case "1": return true;
                    default: success = false; return false;
                }
            }
        }
        success = false;
        return false;
    }

    /// <summary>
    /// Reads the <c>bEnableBodyGen</c> setting. Returns its boolean value; <paramref name="success"/> is true only when the
    /// line was found and parsed to "0"/"1", and <paramref name="lineInIni"/> returns the raw matched line.
    /// </summary>
    public bool GetBodyGenEnabled(List<string> iniContents, out bool success, out string lineInIni)
    {
        success = false;
        lineInIni = GetIniLine(iniContents, "bEnableBodyGen", out bool lineFound);
        if (lineFound)
        {
            string value = GetIniSettingValue(lineInIni, out bool parsed);
            if (parsed)
            {
                success = true;
                switch (value)
                {
                    case "0": return false;
                    case "1": return true;
                    default: success = false; return false;
                }
            }
        }
        success = false;
        return false;
    }

    /// <summary>
    /// Reads the <c>iScaleMode</c> setting as an int in the range 0..3. <paramref name="success"/> is true only when the
    /// line was found and parsed within range; returns -1 (and false) otherwise. <paramref name="lineInIni"/> returns the raw line.
    /// </summary>
    public static int GetScaleMode(List<string> iniContents, out bool success, out string lineInIni)
    {
        success = false;
        lineInIni = GetIniLine(iniContents, "iScaleMode", out bool lineFound);
        if (lineFound)
        {
            string value = GetIniSettingValue(lineInIni, out bool parsed);
            if (parsed && int.TryParse(value, out int result) && result >= 0 && result <= 3)
            {
                success = true;
                return result;
            }
        }
        success = false;
        return -1;
    }

    /// <summary>
    /// Configures the RaceMenu ini for BodyGen: ensures BodyMorph and BodyGen are enabled and forces a RaceMenu-compatible
    /// scale mode (rewriting 0/2 to 1), then writes the file back. Returns false on any read/parse/write failure.
    /// Side effect: rewrites the SKEE ini on disk.
    /// </summary>
    public bool SetRaceMenuIniForBodyGen()
    {
        var iniContents = GetRaceMenuIniContents(out bool success, out string iniFileName);
        if (!success)
        { return false; }

        if (!GetBodyMorphEnabled(iniContents, out success, out string morphLine))
        {
            if (!success)
            {
                return false;
            }
            else
            {
                SetIniSettingValue(morphLine, "1", iniContents, out success);
                if(!success)
                {
                    return false;
                }
            }
        }

        if (!GetBodyGenEnabled(iniContents, out success, out string genLine))
        {
            if (!success)
            {
                return false;
            }
            else
            {
                SetIniSettingValue(genLine, "1", iniContents, out success);
                if(!success)
                {
                    return false;
                }
            }
        }

        int scaleMode = GetScaleMode(iniContents, out success, out string scaleLine);
        if (!success)
        {
            return false;
        }
        else if (scaleMode == 0 || scaleMode == 2)
        {
            SetIniSettingValue(scaleLine, "1", iniContents, out success);
            if (!success)
            {
                return false;
            }
        }

        if (WriteRaceMenuIni(iniContents, iniFileName))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Configures the RaceMenu ini for BodySlide: ensures BodyMorph is enabled and BodyGen is disabled, then writes the file
    /// back. Returns false on any read/parse/write failure. Side effect: rewrites the SKEE ini on disk.
    /// </summary>
    public bool SetRaceMenuIniForBodySlide()
    {
        var iniContents = GetRaceMenuIniContents(out bool success, out string iniFileName);
        if (!success)
        { return false; }

        if (!GetBodyMorphEnabled(iniContents, out success, out string morphLine))
        {
            if (!success)
            {
                return false;
            }
            else
            {
                SetIniSettingValue(morphLine, "1", iniContents, out success);
                if (!success)
                {
                    return false;
                }
            }
        }

        if (GetBodyGenEnabled(iniContents, out success, out string genLine))
        {
            if (!success)
            {
                return false;
            }
            else
            {
                SetIniSettingValue(genLine, "0", iniContents, out success);
                if (!success)
                {
                    return false;
                }
            }
        }

        if (WriteRaceMenuIni(iniContents, iniFileName))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> (joined by newlines) back to the named SKEE ini under the Data SKSE\Plugins folder.
    /// Returns false and logs on failure. Side effect: overwrites the ini file on disk.
    /// </summary>
    public bool WriteRaceMenuIni(List<string> contents, string fileName)
    {
        string iniPath = Path.Combine(_environmentProvider.DataFolderPath, "SKSE", "Plugins", fileName);
        try
        {
            File.WriteAllText(iniPath, string.Join(Environment.NewLine, contents));
            return true;
        }
        catch
        {
            _logger.LogMessage("Could not write to " + iniPath);
            return false;
        }
    }
}