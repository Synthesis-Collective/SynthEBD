using System.Globalization;

namespace SynthEBD;

/// <summary>
/// Parses a BodyGen template's Specs string (e.g. "AnkleSize@0.6:0.4, Arms@0.26, 7B Upper@0.1")
/// into a virtual <see cref="BodySlideSetting"/> so the existing BodySlideDeformer pipeline
/// can preview the template. BodyGen values are applied directly (not weight-blended), so
/// single values collapse to Big==Small and ranges map High->Big, Low->Small — the preview
/// weight slider then scrubs through the random range the patcher will pick from.
///
/// Grammar: comma-separated entries. Each entry is "SliderName@Value" or
/// "SliderName@Low:High". Slider names may contain spaces ("7B Upper"); we split on the
/// last '@' and the first ':' to keep parsing robust. Floats parse with InvariantCulture
/// so comma-decimal locales don't silently corrupt values.
/// </summary>
public static class BodyGenSpecsParser
{
    /// <summary>
    /// Merges multiple template Specs strings into one virtual BodySlideSetting by summing
    /// per-slider values across templates (matches BodyGen runtime behavior where multiple
    /// templates stack additively on the same NPC).
    /// </summary>
    public static BodySlideSetting ParseAndMerge(IEnumerable<string> specsCollection, string sliderGroup, out List<string> errors)
    {
        errors = new List<string>();
        var merged = new BodySlideSetting
        {
            Label = "BodyGen Preview (stacked)",
            SliderGroup = sliderGroup ?? string.Empty,
            SliderValues = new Dictionary<string, BodySlideSlider>()
        };

        if (specsCollection == null) return merged;

        foreach (var specs in specsCollection)
        {
            var single = Parse(specs, sliderGroup, out var singleErrors);
            errors.AddRange(singleErrors);
            foreach (var kv in single.SliderValues)
            {
                if (merged.SliderValues.TryGetValue(kv.Key, out var existing))
                {
                    existing.Big += kv.Value.Big;
                    existing.Small += kv.Value.Small;
                }
                else
                {
                    merged.SliderValues[kv.Key] = new BodySlideSlider
                    {
                        SliderName = kv.Value.SliderName,
                        Big = kv.Value.Big,
                        Small = kv.Value.Small
                    };
                }
            }
        }

        return merged;
    }

    public static BodySlideSetting Parse(string specs, string sliderGroup, out List<string> errors)
    {
        errors = new List<string>();
        var preset = new BodySlideSetting
        {
            Label = "BodyGen Preview",
            SliderGroup = sliderGroup ?? string.Empty,
            SliderValues = new Dictionary<string, BodySlideSlider>()
        };

        if (string.IsNullOrWhiteSpace(specs))
        {
            return preset;
        }

        var entries = specs.Split(',');
        foreach (var raw in entries)
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;

            int atIdx = entry.LastIndexOf('@');
            if (atIdx <= 0 || atIdx == entry.Length - 1)
            {
                errors.Add("Malformed entry (missing '@' or empty name/value): '" + entry + "'");
                continue;
            }

            string name = entry.Substring(0, atIdx).Trim();
            string valuePart = entry.Substring(atIdx + 1).Trim();
            if (name.Length == 0 || valuePart.Length == 0)
            {
                errors.Add("Malformed entry (empty name or value): '" + entry + "'");
                continue;
            }

            float low, high;
            int colonIdx = valuePart.IndexOf(':');
            if (colonIdx < 0)
            {
                if (!float.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    errors.Add("Could not parse value '" + valuePart + "' in '" + entry + "'");
                    continue;
                }
                low = v;
                high = v;
            }
            else
            {
                string lowStr = valuePart.Substring(0, colonIdx).Trim();
                string highStr = valuePart.Substring(colonIdx + 1).Trim();
                if (!float.TryParse(lowStr, NumberStyles.Float, CultureInfo.InvariantCulture, out low) ||
                    !float.TryParse(highStr, NumberStyles.Float, CultureInfo.InvariantCulture, out high))
                {
                    errors.Add("Could not parse range '" + valuePart + "' in '" + entry + "'");
                    continue;
                }
            }

            // Big = High * 100, Small = Low * 100. Deformer math:
            // final = deltaHigh * (w/100) + deltaLow * ((100-w)/100)
            // At w=100 this yields delta*high; at w=0 delta*low. For single values (low==high)
            // the result collapses to delta*V regardless of weight.
            int big = (int)Math.Round(high * 100f);
            int small = (int)Math.Round(low * 100f);

            preset.SliderValues[name] = new BodySlideSlider
            {
                SliderName = name,
                Big = big,
                Small = small
            };
        }

        return preset;
    }
}
