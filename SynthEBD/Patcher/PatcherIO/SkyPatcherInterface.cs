using System.IO; // Path.Combine — previously pulled in via the now-removed `using Microsoft.IO;`
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Converters;

namespace SynthEBD;

public class SkyPatcherInterface
{
    private readonly IOutputEnvironmentStateProvider _environmentStateProvider;
    private readonly PatcherState _patcherState;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;
    private readonly PatcherIO _patcherIO;

    private List<SkyPatcherLine> _lines;

    /// <summary>
    /// One "key=value" SkyPatcher directive. The value is stored either as a literal string
    /// (<see cref="Literal"/>, e.g. a height number) or as a <see cref="FormKey"/>
    /// (<see cref="FormKeyRef"/>) that is formatted at write time. Keeping the FormKey structural
    /// (instead of pre-formatting it into the line) lets <see cref="WriteIni"/> remap output-plugin
    /// FormKeys after an auto-split relocates the surrogate/duplicated records from "&lt;name&gt;.esp"
    /// into "&lt;name&gt;_2.esp" etc.
    /// </summary>
    private readonly struct SkyPatcherDirective
    {
        public string Key { get; }
        public FormKey? FormKeyRef { get; }
        public string? Literal { get; }

        public SkyPatcherDirective(string key, FormKey formKeyRef)
        {
            Key = key;
            FormKeyRef = formKeyRef;
            Literal = null;
        }

        public SkyPatcherDirective(string key, string literal)
        {
            Key = key;
            FormKeyRef = null;
            Literal = literal;
        }
    }

    /// <summary>One ".ini" line: the NPC being filtered on (<c>filterByNPCs</c>) plus its directives.</summary>
    private sealed class SkyPatcherLine
    {
        public FormKey Npc { get; }
        public List<SkyPatcherDirective> Directives { get; } = new();

        public SkyPatcherLine(FormKey npc)
        {
            Npc = npc;
        }
    }

    public SkyPatcherInterface(IOutputEnvironmentStateProvider environmentStateProvider, PatcherState patcherState, SynthEBDPaths paths, Logger logger, PatcherIO patcherIO)
    {
        _environmentStateProvider = environmentStateProvider;
        _patcherState = patcherState;
        _paths = paths;
        _logger = logger;
        _patcherIO = patcherIO;

        Reinitialize();
    }

    public void Reinitialize()
    {
        _lines = new List<SkyPatcherLine>();
        ClearIni();
    }

    // ── Existing methods (unchanged) ──────────────────────────────────────────

    public void ApplyFace(FormKey applyTo, FormKey faceTemplate) // This doesn't work if the face texture isn't baked into the facegen nif. Not useful for SynthEBD.
    {
        if (applyTo.IsNull || faceTemplate.IsNull)
        {
            return;
        }

        var line = new SkyPatcherLine(applyTo);
        line.Directives.Add(new SkyPatcherDirective("copyVisualStyle", faceTemplate));
        _lines.Add(line);
    }

    public void ApplySkin(FormKey applyTo, FormKey skinFk)
    {
        if (applyTo.IsNull || skinFk.IsNull)
        {
            return;
        }

        var line = new SkyPatcherLine(applyTo);
        line.Directives.Add(new SkyPatcherDirective("skin", skinFk));
        _lines.Add(line);
    }

    public void ApplyHeight(FormKey applyTo, float heightFlt)
    {
        var line = new SkyPatcherLine(applyTo);
        line.Directives.Add(new SkyPatcherDirective("height", heightFlt.ToString()));
        _lines.Add(line);
    }

    // ── New methods for truth table compliance ────────────────────────────────

    /// <summary>
    /// Emits a CopyVisualStyle command to transfer FaceGen appearance (face textures
    /// baked into the nif and/or headpart shapes) from a surrogate NPC to the original.
    ///
    /// Used when:
    ///   - Asset Nif mode + SkyPatcher (Cases 13, 16): transfers baked face textures
    ///   - Headpart Nif mode + SkyPatcher (Cases 4, 8, 16): transfers baked headpart shapes
    ///   - Both (Case 16): single CopyVisualStyle transfers everything from the shared surrogate
    /// </summary>
    public void ApplyVisualStyle(FormKey applyTo, FormKey faceTemplate)
    {
        if (applyTo.IsNull || faceTemplate.IsNull)
        {
            return;
        }

        var line = new SkyPatcherLine(applyTo);
        line.Directives.Add(new SkyPatcherDirective("copyVisualStyle", faceTemplate));
        _lines.Add(line);
    }

    /// <summary>
    /// Emits a combined SetSkin + CopyVisualStyle command on a single ini line.
    /// Used when both body textures (via SetSkin) and FaceGen appearance (via
    /// CopyVisualStyle) need to be transferred from a surrogate to the original NPC.
    ///
    /// Applicable to Cases 13 and 16 where Asset mode is Nif + SkyPatcher.
    /// </summary>
    public void ApplySkinAndVisualStyle(FormKey applyTo, FormKey skinFk, FormKey faceTemplate)
    {
        if (applyTo.IsNull || skinFk.IsNull || faceTemplate.IsNull)
        {
            return;
        }

        var line = new SkyPatcherLine(applyTo);
        line.Directives.Add(new SkyPatcherDirective("skin", skinFk));
        line.Directives.Add(new SkyPatcherDirective("copyVisualStyle", faceTemplate));
        _lines.Add(line);
    }

    // ── Output ────────────────────────────────────────────────────────────────

    /// <param name="formKeyRemap">
    /// Optional map of original output-plugin FormKeys to their post-split locations. When the output
    /// plugin was auto-split (see <see cref="PatcherIO.BuildSplitFormKeyRemap"/>), surrogate/duplicated
    /// records move from "&lt;name&gt;.esp" into "&lt;name&gt;_2.esp" etc.; this map rewrites the affected
    /// FormKeys so the .ini keeps pointing at each record's true file. Non-output FormKeys (donor skins,
    /// templates) are absent from the map and therefore left untouched.
    /// </param>
    public void WriteIni(IReadOnlyDictionary<FormKey, FormKey>? formKeyRemap = null)
    {
        string destinationPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
        PatcherIO.CreateDirectoryIfNeeded(destinationPath, PatcherIO.PathType.File);

        var renderedLines = _lines.Select(line => RenderLine(line, formKeyRemap)).ToList();
        Task.Run(() => PatcherIO.WriteTextFile(destinationPath, renderedLines, _logger));
    }

    private void ClearIni()
    {
        string destinationPath = Path.Combine(_paths.OutputDataFolder, "SKSE", "Plugins", "SkyPatcher", "npc", "SynthEBD", "SynthEBD.ini");
        _patcherIO.TryDeleteFile(destinationPath, _logger);
    }

    public bool HasEntries()
    {
        return _lines.Any();
    }

    /// <summary>Renders one line to its final "filterByNPCs=...:key=value:..." text, applying
    /// <paramref name="remap"/> to any output-plugin FormKey that moved during an auto-split.</summary>
    private static string RenderLine(SkyPatcherLine line, IReadOnlyDictionary<FormKey, FormKey>? remap)
    {
        string npc = BodyGenWriter.FormatFormKeyForBodyGen(line.Npc);
        string directives = string.Join(":", line.Directives.Select(d => RenderDirective(d, remap)));
        return "filterByNPCs=" + npc + ":" + directives;
    }

    /// <summary>Renders one directive. Literal directives pass through unchanged; FormKey-valued
    /// directives are formatted here, applying <paramref name="remap"/> first so a split-relocated
    /// output-plugin FormKey resolves to its true file.</summary>
    private static string RenderDirective(SkyPatcherDirective directive, IReadOnlyDictionary<FormKey, FormKey>? remap)
    {
        if (directive.FormKeyRef is FormKey fk)
        {
            if (remap != null && remap.TryGetValue(fk, out var mapped))
            {
                fk = mapped;
            }
            return directive.Key + "=" + BodyGenWriter.FormatFormKeyForBodyGen(fk);
        }
        return directive.Key + "=" + directive.Literal;
    }
}
