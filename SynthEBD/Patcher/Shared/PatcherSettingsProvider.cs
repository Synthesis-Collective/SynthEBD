using System.IO;

namespace SynthEBD;

public class PatcherSettingsProvider
{
    public Lazy<LoadSource?> SourceSettings { get; }

    public PatcherSettingsProvider()
    {
        SourceSettings = new Lazy<LoadSource?>(() =>
        {
            if (File.Exists(Paths.SettingsSourcePath))
            {
                var source = JSONhandler<LoadSource>.LoadJSONFile(Paths.SettingsSourcePath, out bool loadSuccess,
                    out string exceptionStr);
                if (loadSuccess)
                {
                    return source;
                }
                Logger.LogError("Could not load Settings Source. Error: " + exceptionStr);
                return null;
            }
            return null;
        });
    }
}