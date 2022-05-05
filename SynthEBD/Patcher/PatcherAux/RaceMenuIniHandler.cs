using System.IO;

namespace SynthEBD
{
    public class RaceMenuIniHandler
    {
        public static List<string> GetRaceMenuIniContents(out bool success, out string fileName)
        {
            success = false;
            string iniPath64 = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skee64.ini");
            string iniPathVR = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", "skeevr.ini");

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
                Logger.LogMessage("Couldn't access RaceMenu ini at " + iniPath);
            }
            return iniContents;
        }

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

        public static bool GetBodyMorphEnabled(List<string> iniContents, out bool success, out string lineInIni)
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

        public static bool GetBodyGenEnabled(List<string> iniContents, out bool success, out string lineInIni)
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

        public static bool SetRaceMenuIniForBodyGen()
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

        public static bool SetRaceMenuIniForBodySlide()
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

        public static bool WriteRaceMenuIni(List<string> contents, string fileName)
        {
            string iniPath = Path.Combine(PatcherEnvironmentProvider.Environment.DataFolderPath, "SKSE", "Plugins", fileName);
            try
            {
                File.WriteAllText(iniPath, string.Join(Environment.NewLine, contents));
                return true;
            }
            catch
            {
                Logger.LogMessage("Could not write to " + iniPath);
                return false;
            }
        }
    }
}
