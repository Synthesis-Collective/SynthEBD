using Mutagen.Bethesda.Skyrim;
using System.IO;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Builds the per-NPC verbose XML report: an in-memory <see cref="XElement"/> tree assembled as the patcher
/// processes a single NPC, then serialized to disk. Extracted from <see cref="Logger"/> (R12); the report
/// <em>state</em> lives on <see cref="NPCInfo.Report"/>, so this type is effectively stateless apart from its
/// injected dependencies. <see cref="Logger"/> owns one instance and forwards its report methods to it.
/// </summary>
public class NpcReportBuilder
{
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly SynthEBDPaths _paths;
    private readonly Logger _logger;

    /// <summary>Constructs the report builder.</summary>
    /// <param name="environmentProvider">Supplies the link cache for resolving race names in reports.</param>
    /// <param name="paths">Resolved output paths (used for the log folder).</param>
    /// <param name="logger">Owning logger; used for the patch-run timestamp, on-screen notices, and file writes.</param>
    public NpcReportBuilder(IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths, Logger logger)
    {
        _environmentProvider = environmentProvider;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Per-NPC verbose-report state: an in-memory XML tree built up as the patcher processes a single
    /// NPC, plus flags controlling whether that NPC is being logged and whether its report should be saved.
    /// </summary>
    public class NPCReport
    {
        /// <summary>Creates a report for the given NPC, seeding its display name from the NPC's log-id string.</summary>
        /// <param name="npcInfo">The NPC this report describes.</param>
        public NPCReport(NPCInfo npcInfo)
        {
            NameString = npcInfo.LogIDstring;
        }
        public string NameString { get; set; }
        public bool LogCurrentNPC { get; set; } = false;
        public bool SaveCurrentNPCLog { get; set; } = false;
        public System.Xml.Linq.XElement RootElement { get; set; } = null;
        public System.Xml.Linq.XElement CurrentElement { get; set; } = null;
        public Dictionary<System.Xml.Linq.XElement, System.Xml.Linq.XElement> ReportElementHierarchy { get; set; } = new();
        public int CurrentLayer;
    }

    /// <summary>Enables verbose reporting for the given NPC, so subsequent report calls are recorded.</summary>
    /// <param name="npcInfo">The NPC to begin reporting on.</param>
    public void TriggerNPCReporting(NPCInfo npcInfo)
    {
        npcInfo.Report.LogCurrentNPC = true;
    }

    /// <summary>Marks the given NPC's report to be saved to disk, but only if it is already being logged.</summary>
    /// <param name="npcInfo">The NPC whose report should be persisted.</param>
    public void TriggerNPCReportingSave(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            npcInfo.Report.SaveCurrentNPCLog = true;
        }
    }

    /// <summary>Initializes a fresh XML report tree for the NPC (if it is being logged) and records its resolved assets/body/height/head-part races.</summary>
    /// <param name="npcInfo">The NPC to initialize a report for.</param>
    public void InitializeNewReport(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            npcInfo.Report.RootElement = new XElement("Report");
            npcInfo.Report.CurrentElement = npcInfo.Report.RootElement;
            npcInfo.Report.ReportElementHierarchy = new Dictionary<XElement, XElement>();

            LogReport("Patching NPC " + npcInfo.Report.NameString, false, npcInfo);

            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(npcInfo.AssetsRace, out var assetsRaceGetter))
            {
                LogReport("Assets race: " + EditorIDHandler.GetEditorIDSafely(assetsRaceGetter), false, npcInfo); ;
            }
            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(npcInfo.BodyShapeRace, out var bodyRaceGetter))
            {
                LogReport("Body Shape race: " + EditorIDHandler.GetEditorIDSafely(bodyRaceGetter), false, npcInfo);
            }
            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(npcInfo.HeightRace, out var heightRaceGetter))
            {
                LogReport("Height race: " + EditorIDHandler.GetEditorIDSafely(heightRaceGetter), false, npcInfo);
            }
            if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(npcInfo.HeadPartsRace, out var headPartsRaceGetter))
            {
                LogReport("Head Parts race: " + EditorIDHandler.GetEditorIDSafely(headPartsRaceGetter), false, npcInfo);
            }
        }
    }

    /// <summary>Opens a nested report subsection under the current element and descends into it (if the NPC is being logged).</summary>
    /// <param name="header">XML element name for the new subsection.</param>
    /// <param name="npcInfo">The NPC whose report is being built.</param>
    /// <remarks>The parent is remembered in <see cref="NPCReport.ReportElementHierarchy"/> so <see cref="CloseReportSubsection"/> can ascend.</remarks>
    public void OpenReportSubsection(string header, NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            var newElement = new XElement(header);
            npcInfo.Report.ReportElementHierarchy.Add(newElement, npcInfo.Report.CurrentElement);
            npcInfo.Report.CurrentElement.Add(newElement);
            npcInfo.Report.CurrentElement = newElement;
        }
    }

    /// <summary>Appends a message to the NPC's current report subsection (if it is being logged), optionally flagging the report for saving.</summary>
    /// <param name="message">Text to add; multi-line text is split across XML text nodes.</param>
    /// <param name="triggerSave">When <c>true</c>, marks the report to be saved.</param>
    /// <param name="npcInfo">The NPC whose report is being built.</param>
    public void LogReport(string message, bool triggerSave, NPCInfo npcInfo) // detailed operation log; not reflected on screen
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            AddStringToReport(npcInfo.Report.CurrentElement, message);

            if (triggerSave)
            {
                npcInfo.Report.SaveCurrentNPCLog = true;
            }
        }
    }

    /// <summary>Adds <paramref name="value"/> to an XML report element, splitting on newlines and inserting a leading blank line if the element already has content.</summary>
    /// <param name="element">Target report element.</param>
    /// <param name="value">Text to append (trimmed, then split on newlines into separate nodes).</param>
    private void AddStringToReport(XElement element, string value)
    {
        var split = value.Trim().Split(Environment.NewLine);

        if (element.Value.Any())
        {
            element.Add(Environment.NewLine);
        }

        foreach (var item in split)
        {
            element.Add(item);
            element.Add(Environment.NewLine);
        }
    }

    /// <summary>Ascends one level in the report tree, back to the current subsection's parent (if the NPC is being logged).</summary>
    /// <param name="npcInfo">The NPC whose report is being built.</param>
    public void CloseReportSubsection(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            npcInfo.Report.CurrentElement = npcInfo.Report.ReportElementHierarchy[npcInfo.Report.CurrentElement];
        }
    }

    /// <summary>Ascends the report tree until the current element has the given name, leaving that element current.</summary>
    /// <param name="label">XML element name to stop at.</param>
    /// <param name="npcInfo">The NPC whose report is being built.</param>
    /// <remarks>Assumes an ancestor with that name exists; otherwise the dictionary lookup past the root would throw.</remarks>
    public void CloseReportSubsectionsTo(string label, NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            while (npcInfo.Report.CurrentElement.Name != label)
            {
                npcInfo.Report.CurrentElement = npcInfo.Report.ReportElementHierarchy[npcInfo.Report.CurrentElement];
            }
        }
    }

    /// <summary>Ascends the report tree to the element with the given name, then ascends once more to its parent.</summary>
    /// <param name="label">XML element name whose parent should become current.</param>
    /// <param name="npcInfo">The NPC whose report is being built.</param>
    public void CloseReportSubsectionsToParentOf(string label, NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            while (npcInfo.Report.CurrentElement.Name != label)
            {
                npcInfo.Report.CurrentElement = npcInfo.Report.ReportElementHierarchy[npcInfo.Report.CurrentElement];
            }
            npcInfo.Report.CurrentElement = npcInfo.Report.ReportElementHierarchy[npcInfo.Report.CurrentElement];
        }
    }

    /// <summary>Serializes the NPC's report to an indented XML file (when both logging and saving are enabled) and returns the path and contents.</summary>
    /// <param name="npcInfo">The NPC whose report to save.</param>
    /// <returns>A tuple of (output file path, report text); ("", "") when nothing was saved.</returns>
    /// <remarks>Empty reports are skipped with an on-screen note. The file write is fire-and-forget on a background task.</remarks>
    public (string, string) SaveReport(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC && npcInfo.Report.SaveCurrentNPCLog)
        {
            string saveName = IO_Aux.MakeValidFileName(npcInfo.Report.NameString + ".xml");
            string outputFile = System.IO.Path.Combine(_paths.LogFolderPath, _logger.PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), saveName);

            XDocument output = new XDocument();
            output.Add(npcInfo.Report.RootElement);

            string reportStr = LogFormatting.FormatLogStringIndents(output.ToString());
            if (!reportStr.IsNullOrWhitespace())
            {
                Task.Run(() => PatcherIO.WriteTextFile(outputFile, reportStr, _logger));
            }
            else
            {
                _logger.LogMessage("Verbose log for " + npcInfo.LogIDstring + " will not be saved because it is empty");
            }

            return (outputFile, reportStr);
        }
        return ("", "");
    }
}
