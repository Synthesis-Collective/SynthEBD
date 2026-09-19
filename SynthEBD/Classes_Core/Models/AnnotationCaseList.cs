using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SynthEBD;

/// <summary>
/// One entry in a hand-authored worklist for the annotation queue's
/// <see cref="AnnotationQueuePolicy.List"/> mode: which slice to serve, and optionally why it is
/// worth looking at.
/// </summary>
public sealed class AnnotationCase
{
    /// <summary>BodySlide preset label. The only required field.</summary>
    public string PresetLabel { get; set; } = "";

    /// <summary>Weight slot, or null for "every weight this preset has a row at". Omitting the
    /// weight is the natural way to ask for a whole preset to be reviewed.</summary>
    public int? Weight { get; set; }

    /// <summary>Gender, or null to match whichever gender the preset exists under. Almost always
    /// omitted -- a profile's presets are single-gender in practice -- but honored when present so
    /// a list can disambiguate a label that exists on both sides.</summary>
    public Gender? Gender { get; set; }

    /// <summary>
    /// Free text shown while this case is being judged.
    /// <para>This is the field that makes a worklist worth more than a list of names: "sits 0.2
    /// above the Chubby cut", "alias of a preset you called Normal", "the lordosis case from D25".
    /// A reviewer who knows why a case was picked judges it differently -- and more usefully --
    /// than one working through anonymous rows.</para>
    /// </summary>
    public string Note { get; set; } = "";
}

/// <summary>
/// Parses a worklist of <see cref="AnnotationCase"/>s from text. Pure logic with no UI or file-IO
/// dependency, so the formats are unit-testable directly.
///
/// <para>Two input shapes are accepted, detected from the first non-whitespace character, because
/// the two realistic sources want different things. A list pasted out of a chat or typed by hand
/// wants to be plain lines; a list produced by a script -- or a verdict export being fed back in
/// for re-judging -- is already JSON and should not have to be reformatted.</para>
///
/// <para><b>Plain text</b>, one case per line. Blank lines and lines starting with <c>#</c> or
/// <c>//</c> are ignored, so a list can carry its own commentary:</para>
/// <code>
/// # Belly boundary cases, generated 2026-09-18
/// 3BA Willendorf | 75 | Spine_to_BellyFlab 19.36 but sternum_to_belly 0.45
/// Nami Extra Curvy | 100
/// CustomPresetChubby2
/// </code>
/// <para>The separator is <c>|</c> when present, else a tab, else a trailing number is peeled off
/// the end of the line. That last rule is what makes <c>Preset Name 75</c> and
/// <c>Preset Name, 75</c> work without the user having to know a format at all.</para>
///
/// <para><b>JSON</b>, either a bare array of objects or an object with a <c>rows</c> array. Field
/// names are matched case-insensitively and unknown fields are ignored, so
/// <see cref="AnnotationVerdictPayload"/> -- which carries <c>value</c>, <c>values</c> and
/// <c>aliases</c> the worklist has no use for -- can be loaded back unmodified.</para>
/// </summary>
public static class AnnotationCaseList
{
    /// <summary>Peels a trailing weight off a plain-text line that uses no explicit separator:
    /// "Preset Name 75", "Preset Name, 75". Bounded to three digits so a preset whose name ends in
    /// a long number (a version, a date) isn't silently truncated into a weight.</summary>
    private static readonly Regex TrailingWeight = new(@"^(?<label>.*?)[\s,]+(?<weight>\d{1,3})\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses <paramref name="text"/> into cases, in the order they appear -- the order is the
    /// caller's deliberate choice and is never rearranged here.
    /// </summary>
    /// <param name="text">Plain-text or JSON worklist.</param>
    /// <param name="warnings">Per-line / per-entry problems. Populated rather than thrown: one bad
    /// line in a forty-line list should cost that line, not the list.</param>
    /// <returns>The parsed cases. Empty when the input is blank or wholly unparseable.</returns>
    public static List<AnnotationCase> Parse(string text, out List<string> warnings)
    {
        warnings = new List<string>();
        var result = new List<AnnotationCase>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        string trimmed = text.TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            ParseJson(trimmed, result, warnings);
            return result;
        }

        ParsePlainText(text, result, warnings);
        return result;
    }

    private static void ParsePlainText(string text, List<AnnotationCase> result, List<string> warnings)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;

            string[] parts;
            if (line.Contains('|')) parts = line.Split('|');
            else if (line.Contains('\t')) parts = line.Split('\t');
            else parts = null;

            var entry = new AnnotationCase();

            if (parts != null)
            {
                entry.PresetLabel = parts[0].Trim();
                if (parts.Length > 1)
                {
                    string weightToken = parts[1].Trim();
                    if (weightToken.Length > 0)
                    {
                        if (TryParseWeight(weightToken, out int w)) entry.Weight = w;
                        else warnings.Add("Line " + (i + 1) + ": \"" + weightToken + "\" is not a weight 0-100; serving every weight for this preset.");
                    }
                }
                // Everything after the weight is the note, rejoined so a note may itself contain
                // the separator.
                if (parts.Length > 2) entry.Note = string.Join(parts.Length > 0 && line.Contains('|') ? "|" : "\t", parts, 2, parts.Length - 2).Trim();
            }
            else
            {
                var match = TrailingWeight.Match(line);
                if (match.Success && TryParseWeight(match.Groups["weight"].Value, out int w))
                {
                    entry.PresetLabel = match.Groups["label"].Value.Trim();
                    entry.Weight = w;
                }
                else
                {
                    entry.PresetLabel = line;
                }
            }

            if (entry.PresetLabel.Length == 0)
            {
                warnings.Add("Line " + (i + 1) + ": no preset name; skipped.");
                continue;
            }
            result.Add(entry);
        }
    }

    private static void ParseJson(string json, List<AnnotationCase> result, List<string> warnings)
    {
        JToken root;
        try
        {
            root = JToken.Parse(json);
        }
        catch (Exception ex)
        {
            warnings.Add("Not valid JSON: " + ex.Message);
            return;
        }

        JArray rows = root as JArray;
        if (rows == null && root is JObject obj)
        {
            // "rows" is what AnnotationVerdictPayload writes; "cases" reads naturally for a
            // hand-authored worklist. Accept either rather than making the user remember.
            rows = obj["rows"] as JArray ?? obj["Rows"] as JArray
                ?? obj["cases"] as JArray ?? obj["Cases"] as JArray;
        }

        if (rows == null)
        {
            warnings.Add("JSON has no array of cases (expected a bare array, or an object with a \"rows\" array).");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JObject row)
            {
                warnings.Add("Entry " + (i + 1) + " is not an object; skipped.");
                continue;
            }

            var entry = new AnnotationCase
            {
                PresetLabel = (ReadString(row, "preset") ?? ReadString(row, "presetLabel") ?? "").Trim(),
                Note = (ReadString(row, "note") ?? ReadString(row, "reason") ?? "").Trim(),
            };

            string weightToken = ReadString(row, "weight");
            if (!string.IsNullOrWhiteSpace(weightToken))
            {
                if (TryParseWeight(weightToken.Trim(), out int w)) entry.Weight = w;
                else warnings.Add("Entry " + (i + 1) + ": \"" + weightToken + "\" is not a weight 0-100; serving every weight for this preset.");
            }

            string genderToken = ReadString(row, "gender");
            if (!string.IsNullOrWhiteSpace(genderToken))
            {
                if (TryParseGender(genderToken.Trim(), out var g)) entry.Gender = g;
                else warnings.Add("Entry " + (i + 1) + ": \"" + genderToken + "\" is not a gender; matching either.");
            }

            if (entry.PresetLabel.Length == 0)
            {
                warnings.Add("Entry " + (i + 1) + ": no \"preset\" field; skipped.");
                continue;
            }
            result.Add(entry);
        }
    }

    /// <summary>Case-insensitive property read that tolerates a JSON number or bool where a string
    /// is expected, so <c>"weight": 75</c> and <c>"weight": "75"</c> both work.</summary>
    private static string ReadString(JObject row, string name)
    {
        foreach (var property in row.Properties())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (property.Value == null || property.Value.Type == JTokenType.Null) return null;
            return property.Value.Type == JTokenType.String
                ? property.Value.Value<string>()
                : property.Value.ToString();
        }
        return null;
    }

    private static bool TryParseWeight(string token, out int weight)
    {
        weight = 0;
        // Tolerate "w75" / "W75", which is how weights read in the row headers the user is looking
        // at while writing the list.
        if (token.Length > 1 && (token[0] == 'w' || token[0] == 'W')) token = token.Substring(1);
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return false;
        if (parsed < 0 || parsed > 100) return false;
        weight = parsed;
        return true;
    }

    private static bool TryParseGender(string token, out Gender gender)
    {
        gender = SynthEBD.Gender.Female;
        if (token.Equals("f", StringComparison.OrdinalIgnoreCase)
            || token.Equals("female", StringComparison.OrdinalIgnoreCase))
        {
            gender = SynthEBD.Gender.Female;
            return true;
        }
        if (token.Equals("m", StringComparison.OrdinalIgnoreCase)
            || token.Equals("male", StringComparison.OrdinalIgnoreCase))
        {
            gender = SynthEBD.Gender.Male;
            return true;
        }
        return false;
    }
}
