using Mutagen.Bethesda.Plugins;
using System.IO;

namespace SynthEBD;

/// <summary>
/// Emits the BodyGen runtime data files (RaceMenu BodyGen <c>templates.ini</c> and <c>morphs.ini</c>)
/// from the body-shape assignments accumulated in <see cref="Patcher.BodyGenTracker"/> during patching.
/// Runs near the end of the pipeline, after BodyGen selection has populated the tracker.
/// </summary>
public class BodyGenWriter
{
    private readonly PatcherState _patcherState;
    private readonly PatcherIO _patcherIO;
    private readonly Logger _logger;
    /// <summary>Creates a writer with the patcher state, IO helper, and logger needed to emit BodyGen ini files.</summary>
    public BodyGenWriter(PatcherState patcherState, PatcherIO patcherIO, Logger logger)
    {
        _patcherState = patcherState;
        _patcherIO = patcherIO;
        _logger = logger;
    }
    /// <summary>
    /// Compiles the chosen morph templates and per-NPC morph assignments and writes them as
    /// <c>templates.ini</c> and <c>morphs.ini</c> under <paramref name="outputDataFolder"/>'s
    /// BodyGenData folder (named for the patch file). Side effect: creates the output directory and
    /// writes both files asynchronously via <see cref="Task.Run(System.Action)"/>.
    /// </summary>
    public void WriteBodyGenOutputs(BodyGenConfigs bodyGenConfigs, string outputDataFolder)
    {
        string templates = CompileTemplateINI(bodyGenConfigs);
        string morphs = CompileMorphsINI();

        string outputDirPath = Path.Combine(outputDataFolder, "Meshes", "actors", "character", "BodyGenData", _patcherState.GeneralSettings.PatchFileName + ".esp");
        Directory.CreateDirectory(outputDirPath);

        string templatePath = Path.Combine(outputDirPath, "templates.ini");
        string morphsPath = Path.Combine(outputDirPath, "morphs.ini");

        _logger.LogMessage("Writing BodyGen Templates to " + templatePath);
        Task.Run(() => PatcherIO.WriteTextFile(templatePath, templates, _logger));
        _logger.LogMessage("Writing BodyGen Morphs to " + morphsPath);
        Task.Run(() => PatcherIO.WriteTextFile(morphsPath, morphs, _logger));
    }

    /// <summary>
    /// Builds the <c>templates.ini</c> body: for each chosen morph (male then female) looks up its config
    /// by label and emits "Label=Specs" lines for the assigned templates. Returns the concatenated text.
    /// </summary>
    private static string CompileTemplateINI(BodyGenConfigs bodyGenConfigs)
    {
        string output = "";
        foreach (var assignment in Patcher.BodyGenTracker.AllChosenMorphsMale)
        {
            var currentConfig = bodyGenConfigs.Male.Where(x => x.Label == assignment.Key).First(); // first instead of single in case user has duplicate configs installed
            var assignedTemplates = currentConfig.Templates.Where(x => assignment.Value.Contains(x.Label)).ToArray();

            foreach (var template in assignedTemplates)
            {
                output += template.Label + "=" + template.Specs + Environment.NewLine; // trailing blank line is required by Bodygen
            }
        }

        foreach (var assignment in Patcher.BodyGenTracker.AllChosenMorphsFemale)
        {
            var currentConfig = bodyGenConfigs.Female.Where(x => x.Label == assignment.Key).First();  // first instead of single in case user has duplicate configs installed
            var assignedTemplates = currentConfig.Templates.Where(x => assignment.Value.Contains(x.Label)).ToArray();

            foreach (var template in assignedTemplates)
            {
                output += template.Label + "=" + template.Specs + Environment.NewLine; // trailing blank line is required by Bodygen
            }
        }

        return output;
    }

    /// <summary>
    /// Builds the <c>morphs.ini</c> body: one "FormKey=morph1,morph2,..." line per NPC assignment in
    /// <see cref="Patcher.BodyGenTracker"/>, using the BodyGen FormKey format. Returns the concatenated text.
    /// </summary>
    private static string CompileMorphsINI()
    {
        string output = "";
        foreach (var npcAssignment in Patcher.BodyGenTracker.NPCAssignments)
        {
            output += FormatFormKeyForBodyGen(npcAssignment.Key) + "=";
            for (int i = 0; i < npcAssignment.Value.Count; i++)
            {
                output += npcAssignment.Value[i];
                if (i < npcAssignment.Value.Count - 1)
                {
                    output += ",";
                }
            }
            output += Environment.NewLine; // trailing blank line is required by Bodygen
        }
        return output;
    }

    /// <summary>
    /// Formats a <paramref name="FK"/> as the "ModKey|FormID" string BodyGen expects, with leading
    /// zeros stripped from the FormID (e.g. "Skyrim.esm|14"). Also reused for OBody/AutoBody ini output.
    /// </summary>
    public static string FormatFormKeyForBodyGen(FormKey FK)
    {
        return FK.ModKey.ToString() + "|" + FK.IDString().TrimStart(new Char[] { '0' });
    }
}