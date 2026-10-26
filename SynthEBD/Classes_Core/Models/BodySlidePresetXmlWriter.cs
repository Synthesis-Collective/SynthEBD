using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace SynthEBD;

/// <summary>
/// Writes one BodySlide preset into a SliderPresets XML file -- the format SynthEBD reads at startup
/// (<see cref="Settings_OBody.ImportBodySlides"/>): <c>&lt;SliderPresets&gt;&lt;Preset name set&gt;</c> holding
/// <c>&lt;Group name/&gt;</c> tags and one <c>&lt;SetSlider name size="small|big" value/&gt;</c> per endpoint.
/// Used by the Auto-Annotation Sliders tab's Save / Save As.
/// <para>An existing file is copied to a timestamped <c>.bak</c> next to it before it is changed. An
/// existing preset of the target name keeps its attributes and Group tags; only its SetSlider entries
/// are replaced. Every other preset in the file is left as it was.</para>
/// </summary>
public static class BodySlidePresetXmlWriter
{
    /// <summary>The <c>set</c> attribute and Group tag names of <paramref name="presetName"/> in
    /// <paramref name="xmlPath"/>, so Save As can carry them to the new preset. Empty when the file or
    /// preset can't be read.</summary>
    public static (string? SetName, List<string> Groups) ReadPresetMetadata(string xmlPath, string presetName)
    {
        try
        {
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath)) return (null, new List<string>());
            var preset = FindPreset(XDocument.Load(xmlPath), presetName);
            if (preset == null) return (null, new List<string>());
            return (preset.Attribute("set")?.Value,
                preset.Elements("Group").Select(g => g.Attribute("name")?.Value).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList());
        }
        catch
        {
            return (null, new List<string>());
        }
    }

    /// <summary>
    /// Writes <paramref name="sliders"/> as preset <paramref name="presetName"/> into
    /// <paramref name="xmlPath"/> (created if missing). A new preset gets <paramref name="setName"/>
    /// and <paramref name="groups"/>. Returns the backup path, or "" when the file was new.
    /// </summary>
    public static string WritePreset(string xmlPath, string presetName, IReadOnlyDictionary<string, BodySlideSlider> sliders,
        string? setName = null, IEnumerable<string>? groups = null)
    {
        if (string.IsNullOrWhiteSpace(xmlPath)) throw new ArgumentException("No SliderPresets file given.", nameof(xmlPath));
        if (string.IsNullOrWhiteSpace(presetName)) throw new ArgumentException("No preset name given.", nameof(presetName));

        XDocument doc;
        string backupPath = "";
        if (File.Exists(xmlPath))
        {
            doc = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
            backupPath = xmlPath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
            File.Copy(xmlPath, backupPath, overwrite: true);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
            doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("SliderPresets"));
        }

        var root = doc.Element("SliderPresets");
        if (root == null)
        {
            throw new InvalidDataException(xmlPath + " is not a BodySlide SliderPresets file.");
        }

        var preset = FindPreset(doc, presetName);
        if (preset == null)
        {
            preset = new XElement("Preset", new XAttribute("name", presetName));
            if (!string.IsNullOrEmpty(setName)) preset.Add(new XAttribute("set", setName));
            foreach (var group in groups ?? Enumerable.Empty<string>())
            {
                preset.Add(new XElement("Group", new XAttribute("name", group)));
            }
            root.Add(preset);
        }
        else
        {
            preset.Elements("SetSlider").Remove();
        }

        foreach (var (name, slider) in sliders.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (slider == null || string.IsNullOrEmpty(name)) continue;
            preset.Add(new XElement("SetSlider", new XAttribute("name", name), new XAttribute("size", "small"), new XAttribute("value", FormatValue(slider.Small))));
            preset.Add(new XElement("SetSlider", new XAttribute("name", name), new XAttribute("size", "big"), new XAttribute("value", FormatValue(slider.Big))));
        }

        doc.Save(xmlPath);
        return backupPath;
    }

    /// <summary>Whole values as integers (BodySlide's own style), others with up to six decimals,
    /// always with "." as the separator.</summary>
    public static string FormatValue(float value)
    {
        if (Math.Abs(value - Math.Round(value)) < 1e-6) return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static XElement? FindPreset(XDocument doc, string presetName)
        => doc.Element("SliderPresets")?.Elements("Preset")
            .FirstOrDefault(p => string.Equals(p.Attribute("name")?.Value, presetName, StringComparison.Ordinal));
}
