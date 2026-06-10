using System.Reactive;
using System.Reactive.Subjects;
using Mutagen.Bethesda.Skyrim;
using System.Text;
using System.Windows.Media;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Reflection;
using System.IO;
using System.Collections.ObjectModel;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;

namespace SynthEBD;

/// <summary>Destination/format for log output: the in-app SynthEBD log pane, or the Synthesis console.</summary>
public enum LogMode
{
    /// <summary>Standalone mode — messages go to the on-screen log collection.</summary>
    SynthEBD,
    /// <summary>Synthesis-pipeline mode — messages go to the console.</summary>
    Synthesis
}
/// <summary>
/// Central logging, run-status, and per-NPC verbose-report facility (and itself a view model for the
/// status/log UI). Provides thread-safe appends to the bound log collection, colored status text, the
/// patcher elapsed-time timer, the startup timing log, and a large family of static helpers that format
/// records / subgroups / races / attributes into human-readable strings.
/// </summary>
public sealed class Logger : VM
{
    private readonly DisplayedItemVm _displayedItemVm;
    private readonly VM_LogDisplay _logDisplay;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherIO _patcherIO;
    private SynthEBDPaths _paths;

    private List<string> _startupLog = new();
    private Dictionary<string, System.Diagnostics.Stopwatch> _startupTimers = new();
    private int _startupLogIndentCount = 0;
    public string StatusString { get; set; }
    public string BackupStatusString { get; set; }
    public ObservableCollection<string> LoggedEvents { get; set; } = new();
    /// <summary>The full on-screen log as a single newline-joined string (a live projection of <see cref="LoggedEvents"/>).</summary>
    public string LogString => string.Join(Environment.NewLine, LoggedEvents);
    public SolidColorBrush StatusColor { get; set; }
    public SolidColorBrush BackupStatusColor { get; set; }

    public SolidColorBrush ReadyColor = CommonColors.Green;
    public SolidColorBrush WarningColor = CommonColors.Yellow;
    public SolidColorBrush ErrorColor = CommonColors.Red;
    public string ReadyString = "Ready To Patch";

    private readonly Subject<Unit> _loggedError = new();
    /// <summary>Fires whenever an error is logged, letting the UI react (e.g. flash the status indicator).</summary>
    public IObservable<Unit> LoggedError => _loggedError;

    public DateTime PatcherExecutionStart { get; set; }

    System.Windows.Threading.DispatcherTimer UpdateTimer { get; set; } = new();
    System.Diagnostics.Stopwatch EllapsedTimer { get; set; } = new();

    public NPCInfo CurrentNPCInfo { get; set; } = null;

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

    /// <summary>Constructs the logger and initializes the status line to the ready state.</summary>
    /// <param name="patcherIO">File-writing helper used to persist logs and reports.</param>
    /// <param name="environmentProvider">Supplies the logger mode, link cache, and startup log.</param>
    /// <param name="paths">Resolved output paths (used for the log folder).</param>
    public Logger(PatcherIO patcherIO, IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths)
    {
        StatusColor = ReadyColor;
        StatusString = ReadyString;
        _patcherIO = patcherIO;
        _environmentProvider = environmentProvider;
        _paths = paths;
    }

    // LoggedEvents is a plain ObservableCollection bound to WPF (via VM_LogDisplay's
    // ToObservableChangeSet → DispString). Mutating it from a background thread fires
    // CollectionChanged on that thread and the WPF binding pipeline throws cross-thread —
    // silently, because by then we're outside any await chain. Marshal every mutation to
    // the UI dispatcher so background callers (NPC mesh resolver in Task.Run, etc.) are
    // safe even when several fire concurrently.
    /// <summary>Runs <paramref name="action"/> on the WPF UI thread, executing inline if already on it (or if no dispatcher exists).</summary>
    /// <param name="action">The (typically collection-mutating) action to marshal.</param>
    /// <remarks>See the block comment above: the bound <see cref="LoggedEvents"/> may only be mutated on the UI thread.</remarks>
    private static void OnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>Appends a message to the on-screen log (SynthEBD mode) or writes it to the console (Synthesis mode).</summary>
    /// <param name="message">The message to log.</param>
    public void LogMessage(string message)
    {
        switch (_environmentProvider.LoggerMode)
        {
            case LogMode.SynthEBD: OnUiThread(() => LoggedEvents.Add(message)); break;
            case LogMode.Synthesis: Console.WriteLine(message); break;
        }
    }

    /// <summary>Logs each message in sequence via <see cref="LogMessage(string)"/>.</summary>
    /// <param name="messages">Messages to log.</param>
    public void LogMessage(IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            LogMessage(message);
        }
    }

    /// <summary>Clears the on-screen log collection (marshaled to the UI thread).</summary>
    public void Clear()
    {
        OnUiThread(LoggedEvents.Clear);
    }
    
    /// <summary>Switches the "ready" status text to the message shown when running as a Synthesis settings UI.</summary>
    public void SetSynthesisStartupString()
    {
        ReadyString = "When finished changing settings, close this UI and run your Synthesis patcher";
        StatusString = ReadyString;
    }

    private static readonly object LockStartupLogMethod = new object();

    /// <summary>Marks the start of a timed startup event, increasing the indent level and starting a stopwatch keyed by <paramref name="message"/>.</summary>
    /// <param name="message">Label identifying the event; also the stopwatch key.</param>
    /// <remarks>Only the first start for a given label is timed; a duplicate label is ignored while still incrementing the indent. Thread-safe via an internal lock.</remarks>
    public void LogStartupEventStart(string message)
    {
        lock (LockStartupLogMethod)
        {
            //_startupLog.Add(FormatTimeStamp(DateTime.Now) + GetIndentString() + message);
            // Only the first start for a given label is timed; tie the indent increment to that same
            // condition so it balances End's guarded decrement (a duplicate start no longer drifts the indent).
            if (!_startupTimers.ContainsKey(message))
            {
                _startupLogIndentCount++;
                System.Diagnostics.Stopwatch sw = new();
                sw.Start();
                _startupTimers.Add(message, sw);
            }
        }
    }

    /// <summary>Appends a free-form, timestamped, indented line to the startup log without timing it.</summary>
    /// <param name="message">The line to record.</param>
    public void LogStartupEventInsert(string message)
    {
        _startupLog.Add(FormatTimeStamp(DateTime.Now) + GetIndentString() + message);
    }

    /// <summary>Stops the stopwatch started by <see cref="LogStartupEventStart"/>, decrements the indent, and records the elapsed time if it exceeded 5 ms.</summary>
    /// <param name="message">Label matching the corresponding <see cref="LogStartupEventStart"/> call.</param>
    /// <remarks>Sub-5 ms events are timed but not written, to keep the startup log concise. Thread-safe via an internal lock.</remarks>
    public void LogStartupEventEnd(string message)
    {
        lock (LockStartupLogMethod)
        {
            if (_startupTimers.ContainsKey(message))
            {
                var sw = _startupTimers[message];
                sw.Stop();
                if (_startupLogIndentCount > 0)
                {
                    _startupLogIndentCount--;
                }
                if (sw.ElapsedMilliseconds > 5)
                {
                    _startupLog.Add(FormatTimeStamp(DateTime.Now) + GetIndentString() + "Completed " + message + " in: " + string.Format("{0:D2}:{1:D2}:{2:D2}:{3:D2}", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds, sw.Elapsed.Milliseconds));
                }
                _startupTimers.Remove(message);
            }
        }
    }

    /// <summary>Returns a string of tab characters matching the current startup-log indent depth.</summary>
    /// <returns>Zero or more tab characters.</returns>
    public string GetIndentString()
    {
        return new string('\t', _startupLogIndentCount);
    }

    /// <summary>Formats a timestamp as a bracketed "[HH:MM:SS] " prefix for log lines.</summary>
    /// <param name="dt">The time to format.</param>
    /// <returns>e.g. "[14:03:09] ".</returns>
    public static string FormatTimeStamp(DateTime dt)
    {
        return "[" + DateTimeToHMS(dt) + "] ";
    }
    /// <summary>Formats the time-of-day portion of <paramref name="dt"/> as zero-padded "HH:MM:SS".</summary>
    /// <param name="dt">The time to format.</param>
    /// <returns>e.g. "14:03:09".</returns>
    public static string DateTimeToHMS(DateTime dt)
    {
        return string.Format("{0:D2}:{1:D2}:{2:D2}", dt.Hour, dt.Minute, dt.Second);
    }

    /// <summary>Prepends the environment's startup log, appends any events never marked complete, and writes the combined startup log to <c>StartupLog.txt</c>.</summary>
    /// <remarks>The file write is fire-and-forget on a background task.</remarks>
    public void WriteStartupLog()
    {
        _startupLog.InsertRange(0, _environmentProvider.StartUpLog);

        if (_startupTimers.Any())
        {
            _startupLog.Add("The following events were never logged as completed:");
            foreach (var entry in _startupTimers)
            {
                _startupLog.Add(entry.Key);
            }
        }

        string path = Path.Combine(_paths.LogFolderPath, "StartupLog.txt");
        Task.Run(() => PatcherIO.WriteTextFile(path, _startupLog, this));
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
            string outputFile = System.IO.Path.Combine(_paths.LogFolderPath, PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), saveName);

            XDocument output = new XDocument();
            output.Add(npcInfo.Report.RootElement);

            string reportStr = FormatLogStringIndents(output.ToString());
            if (!reportStr.IsNullOrWhitespace())
            {
                Task.Run(() => PatcherIO.WriteTextFile(outputFile, reportStr, this));
            }
            else
            {
                LogMessage("Verbose log for " + npcInfo.LogIDstring + " will not be saved because it is empty");
            }
            
            return (outputFile, reportStr);
        }
        return ("", "");
    }

    /// <summary>Pretty-prints an XML report string by inserting line breaks between adjacent tags and indenting each line by tag depth.</summary>
    /// <param name="s">Serialized XML (typically from <see cref="System.Xml.Linq.XDocument.ToString()"/>).</param>
    /// <returns>The same XML with tab indentation reflecting element nesting.</returns>
    /// <remarks>Hand-rolled formatter that splits on "&gt;&lt;", so text content containing that sequence could be mis-split. An <see cref="System.Xml.XmlWriter"/> with indentation enabled would be more robust.</remarks>
    public static string FormatLogStringIndents(string s)
    {
        int indent = 0;

        s = s.Replace("><", ">" + Environment.NewLine + "<");

        string[] split = s.Split(Environment.NewLine);
        for (int i = 0; i < split.Length; i++)
        {
            if (split[i].Trim().StartsWith("</"))
            {
                indent--;
                split[i] = Indent(split[i], indent);
            }
            else if (split[i].Trim().StartsWith('<') && !split[i].Trim().EndsWith("/>"))
            {
                split[i] = Indent(split[i], indent);
                indent++;
            }
            else
            {
                split[i] = Indent(split[i], indent);
            }
        }

        return string.Join(Environment.NewLine, split);
    }

    /// <summary>Prefixes <paramref name="s"/> with <paramref name="count"/> tab characters.</summary>
    /// <param name="s">Line to indent.</param>
    /// <param name="count">Number of tabs to prepend (values &lt;= 0 leave the string unchanged).</param>
    /// <returns>The indented line.</returns>
    private static string Indent(string s, int count)
    {
        if (count <= 0) { return s; }
        return new string('\t', count) + s;
    }

    /// <summary>Logs an error to the on-screen log or console (per mode) and raises <see cref="LoggedError"/>.</summary>
    /// <param name="error">The error text.</param>
    public void LogError(string error)
    {
        switch (_environmentProvider.LoggerMode)
        {
            case LogMode.SynthEBD: OnUiThread(() => LoggedEvents.Add(error)); break;
            case LogMode.Synthesis: Console.WriteLine(error); break;
        }
        _loggedError.OnNext(Unit.Default);
    }
    /// <summary>Builds a multi-line listing of an asset pack's subgroups (one line per position, each showing that position's candidate subgroup IDs), optionally indenting one position.</summary>
    /// <param name="ap">The flattened asset pack to describe.</param>
    /// <param name="index">Subgroup position to optionally highlight with indentation.</param>
    /// <param name="indentAtIndex">When <c>true</c>, indents the line at <paramref name="index"/>.</param>
    /// <returns>A newline-delimited description used in verbose logs.</returns>
    public static string SpreadFlattenedAssetPack(FlattenedAssetPack ap, int index, bool indentAtIndex)
    {
        string spread = Environment.NewLine;
        for (int i = 0; i < ap.Subgroups.Count; i++)
        {
            if (indentAtIndex && i == index) { spread += "\t"; }
            spread += i + ": [" + String.Join(',', ap.Subgroups[i].Select(x => x.Id)) + "]" + Environment.NewLine;
        }
        return spread;
    }

    /// <summary>Logs an error and reflects it in the status line, coloring it as a warning or error (errors also raise <see cref="LoggedError"/>).</summary>
    /// <param name="error">The error text (also shown as the status).</param>
    /// <param name="type">Whether this is a warning or an error.</param>
    public void LogErrorWithStatusUpdate(string error, ErrorType type)
    {
        OnUiThread(() => LoggedEvents.Add(error));
        //LogString += error + Environment.NewLine;
        StatusString = error;
        switch (type)
        {
            case ErrorType.Warning:
                StatusColor = WarningColor;
                break;
            case ErrorType.Error:
                _loggedError.OnNext(Unit.Default);
                StatusColor = ErrorColor;
                break;
        }
    }

    /// <summary>Sets the status text, optionally coloring it as a warning.</summary>
    /// <param name="message">Status text.</param>
    /// <param name="triggerWarning">When <c>true</c>, sets the warning color.</param>
    public void UpdateStatus(string message, bool triggerWarning)
    {
        StatusString = message;
        if (triggerWarning)
        {
            StatusColor = WarningColor;
        }
    }

    /// <summary>Sets the status text and its color.</summary>
    /// <param name="message">Status text.</param>
    /// <param name="newColor">Color for the status text.</param>
    public void UpdateStatus(string message, SolidColorBrush newColor)
    {
        StatusString = message;
        StatusColor = newColor;
    }

    /// <summary>Asynchronously sets the status text (optionally as a warning) by offloading to a background task.</summary>
    /// <param name="message">Status text.</param>
    /// <param name="triggerWarning">When <c>true</c>, sets the warning color.</param>
    public async Task UpdateStatusAsync(string message, bool triggerWarning)
    {
        await Task.Run(() => _UpdateStatusAsync(message, triggerWarning));
    }

    /// <summary>Worker for <see cref="UpdateStatusAsync"/>; sets the status fields (no awaitable work — runs synchronously inside the offloaded task).</summary>
    /// <param name="message">Status text.</param>
    /// <param name="triggerWarning">When <c>true</c>, sets the warning color.</param>
    private async Task _UpdateStatusAsync(string message, bool triggerWarning)
    {
        StatusString = message;
        if (triggerWarning)
        {
            StatusColor = WarningColor;
        }
    }

    /// <summary>Asynchronously snapshots the current status text/color into the backup fields.</summary>
    public async Task ArchiveStatusAsync()
    {
        await Task.Run(() => _ArchiveStatusAsync());
    }

    /// <summary>Worker for <see cref="ArchiveStatusAsync"/>; copies the status into the backup fields.</summary>
    private async Task _ArchiveStatusAsync()
    {
        BackupStatusString = StatusString;
        BackupStatusColor = StatusColor;
    }
    /// <summary>Snapshots the current status text/color into the backup fields so it can be restored later.</summary>
    public void ArchiveStatus()
    {
        BackupStatusString = StatusString;
        BackupStatusColor = StatusColor;
    }

    /// <summary>Asynchronously restores the status text/color from the backup fields.</summary>
    public async Task UnarchiveStatusAsync()
    {
        await Task.Run(() => _DeArchiveStatusAsync());
    }

    /// <summary>Worker for <see cref="UnarchiveStatusAsync"/>; restores the status from the backup fields.</summary>
    private async Task _DeArchiveStatusAsync()
    {
        StatusString = BackupStatusString;
        StatusColor = BackupStatusColor;
    }

    /// <summary>Restores the status text/color from the backup fields.</summary>
    public void UnarchiveStatus()
    {
        StatusString = BackupStatusString;
        StatusColor = BackupStatusColor;
    }

    /// <summary>Fire-and-forget wrapper that shows a timed error status on a background task without blocking the caller.</summary>
    /// <param name="error">Status/error text.</param>
    /// <param name="type">Warning vs error styling.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    public void CallTimedLogErrorWithStatusUpdateAsync(string error, ErrorType type, int durationSec)
    {
        Task.Run(() => TimedLogErrorWithStatusUpdateAsync(error, type, durationSec));
    }

    /// <summary>Fire-and-forget wrapper that shows a timed notification status on a background task.</summary>
    /// <param name="message">Status text.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec)
    {
        Task.Run(() => TimedNotifyStatusUpdateAsync(message, durationSec));
    }

    /// <summary>Fire-and-forget wrapper that shows a timed, custom-colored notification status on a background task.</summary>
    /// <param name="message">Status text.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    /// <param name="textColor">Color for the status text.</param>
    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec, SolidColorBrush textColor)
    {
        Task.Run(() => TimedNotifyStatusUpdateAsync(message, durationSec, textColor));
    }

    /// <summary>Archives the status, shows an error for <paramref name="durationSec"/> seconds, then restores it (awaitable, non-blocking).</summary>
    /// <param name="error">Status/error text.</param>
    /// <param name="type">Warning vs error styling.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    private async Task TimedLogErrorWithStatusUpdateAsync(string error, ErrorType type, int durationSec)
    {
        ArchiveStatus();
        LogErrorWithStatusUpdate(error, type);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    /// <summary>Archives the status, shows a notification for <paramref name="durationSec"/> seconds, then restores it (awaitable, non-blocking).</summary>
    /// <param name="message">Status text.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    private async Task TimedNotifyStatusUpdateAsync(string message, int durationSec)
    {
        ArchiveStatus();
        UpdateStatus(message, false);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    /// <summary>Archives the status, shows a custom-colored notification for <paramref name="durationSec"/> seconds, then restores it (awaitable, non-blocking).</summary>
    /// <param name="message">Status text.</param>
    /// <param name="durationSec">Display duration, in seconds.</param>
    /// <param name="textColor">Color for the status text.</param>
    private async Task TimedNotifyStatusUpdateAsync(string message, int durationSec, SolidColorBrush textColor)
    {
        ArchiveStatus();
        UpdateStatus(message, textColor);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    /// <summary>Resets the status line to the ready text and color.</summary>
    public void ClearStatusError()
    {
        StatusString = ReadyString;
        StatusColor = ReadyColor;
    }
    /// <summary>Starts the patch-run timers: a 1-second UI dispatcher timer (forced onto the UI thread) that refreshes the elapsed-time status, plus the elapsed-time stopwatch.</summary>
    public void StartTimer()
    {
        UpdateTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, System.Windows.Application.Current.Dispatcher); // arguments here are forcing the dispatcher to run on the UI thread (otherwise UpdateTimer.Tick fires on a different thread and gets missed by the UI, so the event handler is never called).
        EllapsedTimer = new System.Diagnostics.Stopwatch();
        UpdateTimer.Interval = TimeSpan.FromSeconds(1);
        UpdateTimer.Tick += timer_Tick;
        UpdateTimer.Start();
        EllapsedTimer.Start();
    }

    /// <summary>Stops both the elapsed-time stopwatch and the UI refresh timer.</summary>
    public void StopTimer()
    {
        EllapsedTimer.Stop();
        UpdateTimer.Stop();
    }

    /// <summary>Dispatcher-timer tick handler: updates the status line with the current elapsed patch time.</summary>
    private void timer_Tick(object sender, EventArgs e)
    {
        UpdateStatus("Patching: " + GetEllapsedTime(), false);
    }

    /// <summary>Returns the elapsed patch time formatted as "HH:MM:SS".</summary>
    /// <returns>The elapsed-time string.</returns>
    public string GetEllapsedTime()
    {
        TimeSpan ts = EllapsedTimer.Elapsed;
        return string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds);
    }

    /// <summary>Builds a "Name | EditorID | FormKey" identifier string for an NPC.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <param name="logger">Logger used for safe name resolution.</param>
    /// <returns>A pipe-delimited identifier for logs.</returns>
    public static string GetNPCLogNameString(INpcGetter npc, Logger logger)
    {
        return NameHandler.GetNPCNameSafely(npc, logger) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    /// <summary>Instance overload of <see cref="GetNPCLogNameString(INpcGetter, Logger)"/> using this logger.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <returns>A pipe-delimited "Name | EditorID | FormKey" identifier.</returns>
    public string GetNPCLogNameString(INpcGetter npc)
    {
        return NameHandler.GetNPCNameSafely(npc, this) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    /// <summary>Builds a filesystem-safe "Name (EditorID) FormKey" string for naming per-NPC report files.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <returns>A sanitized identifier safe for use as a file name.</returns>
    public static string GetNPCLogReportingString(INpcGetter npc)
    {
        return IO_Aux.MakeValidFileName(npc.Name?.String + " (" + EditorIDHandler.GetEditorIDSafely(npc) + ") " + npc.FormKey.ToString().Replace(':', '-'));
    }

    /// <summary>Formats a flattened subgroup as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(FlattenedSubgroup subgroup)
    {
        return subgroup.Id + ": " + subgroup.Name;
    }

    /// <summary>Formats an asset-pack subgroup model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(AssetPack.Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a subgroup view model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(VM_Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a placeholder subgroup view model as "ID: Name".</summary>
    /// <param name="subgroup">The subgroup to describe.</param>
    /// <returns>The "ID: Name" string.</returns>
    public static string GetSubgroupIDString(VM_SubgroupPlaceHolder subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    /// <summary>Formats a category-to-values descriptor map as "Category: [v1, v2] | Category2: [...]".</summary>
    /// <param name="descriptorList">Map of descriptor category to its values.</param>
    /// <returns>A single-line, pipe-delimited descriptor summary.</returns>
    public static string GetBodyShapeDescriptorString(Dictionary<string, HashSet<string>> descriptorList)
    {
        List<string> sections = new List<string>();
        foreach (var descriptor in descriptorList)
        {
            string section = descriptor.Key + ": [";
            section += string.Join(", ", descriptor.Value);
            section += "]";
            sections.Add(section);
        }
        return string.Join(" | ", sections);
    }

    /// <summary>Formats a set of body-shape descriptor label signatures, grouping values by category, one category per line.</summary>
    /// <typeparam name="T">A body-shape descriptor label-signature type.</typeparam>
    /// <param name="descriptors">The descriptors to format.</param>
    /// <returns>A multi-line "Category: [values]" summary.</returns>
    public static string GetBodyShapeDescriptorString<T>(HashSet<T> descriptors)
        where T : BodyShapeDescriptor.LabelSignature
    {
        var categories = descriptors.Select(x => x.Category).ToHashSet();
        List<string> desc = new();
        foreach (var category in categories)
        {
            var values = descriptors.Where(x => x.Category == category)?.Select(x => x.Value);
            desc.Add(category + ": [" + String.Join(", ", values) + "]");
        }

        return String.Join(Environment.NewLine, desc);
    }

    /// <summary>Formats a list of race FormKeys as a bracketed, comma-separated list of display names.</summary>
    /// <param name="formKeys">Race FormKeys to format.</param>
    /// <param name="lk">Link cache for resolution.</param>
    /// <param name="patcherState">Patcher state (controls verbose naming detail).</param>
    /// <returns>e.g. "[Nord, Imperial, Snow Elf]".</returns>
    public static string GetRaceListLogStrings(IEnumerable<FormKey> formKeys, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        return "[" + String.Join(", ", formKeys.Select(x => GetRaceLogString(x, lk, patcherState))) + "]";
    }

    /// <summary>Races whose friendly display name differs from their record name, keyed by FormKey.</summary>
    /// <remarks>Keyed by <c>.FormKey</c>: the <c>Mutagen...FormKeys.SkyrimSE...Race.*</c> members are
    /// <c>FormLink</c>s, not <c>FormKey</c>s, so a direct <c>fk.Equals(formLink)</c> is always false (B57).</remarks>
    private static readonly Dictionary<FormKey, string> _specialCaseRaceLogNames = new()
    {
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.DA13AfflictedRace.FormKey, "Afflicted" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Race.NordRaceAstrid.FormKey, "Astrid Race" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.SnowElfRace.FormKey, "Snow Elf" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dawnguard.Race.DLC1NordRace.FormKey, "Nord (Dawnguard)" },
        { Mutagen.Bethesda.FormKeys.SkyrimSE.Dragonborn.Race.DLC2MiraakRace.FormKey, "Nord (Miraak)" },
    };

    /// <summary>Returns the curated display name for a race whose friendly name differs from its record name, if one is defined.</summary>
    /// <param name="raceFormKey">The race FormKey to look up.</param>
    /// <param name="name">Receives the curated name when present.</param>
    /// <returns><c>true</c> if a curated special-case name exists for the race.</returns>
    public static bool TryGetSpecialCaseRaceLogName(FormKey raceFormKey, out string name)
    {
        return _specialCaseRaceLogNames.TryGetValue(raceFormKey, out name);
    }

    /// <summary>Resolves a single race FormKey to a friendly display name, with special cases for races whose display name differs from their record name.</summary>
    /// <param name="fk">The race FormKey.</param>
    /// <param name="lk">Link cache for resolution.</param>
    /// <param name="patcherState">Patcher state; when detailed-attribute verbosity is off, returns the raw FormKey string.</param>
    /// <returns>A display name (appending " Vampire" for vampire variants), or a fallback when not in the load order.</returns>
    public static string GetRaceLogString(FormKey fk, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        if (!patcherState.GeneralSettings.VerboseModeDetailedAttributes)
        {
            return fk.ToString();
        }

        // specific races whose display names aren't the same as their "real" names
        if (TryGetSpecialCaseRaceLogName(fk, out var specialCaseName))
        {
            return specialCaseName;
        }

        // general handling
        if (lk.TryResolve<IRaceGetter>(fk, out var raceGetter))
        {
            var edid = EditorIDHandler.GetEditorIDSafely(raceGetter);
            if (raceGetter.Name != null && !raceGetter.Name.ToString().IsNullOrWhitespace())
            {
                var name = raceGetter.Name.ToString();
                if (edid.Contains("Vampire", StringComparison.OrdinalIgnoreCase))
                {
                    name += " Vampire";
                }
                return name;
            }
            else
            {
                return edid;
            }
        }
        else
        {
            return "(Not Currently In Load Order)";
        }
    }

    /// <summary>Builds a one-line "&lt;status&gt; Races: ..." report listing the given races by EditorID, if any.</summary>
    /// <param name="allowStatus">Prefix describing the rule (e.g. "Allowed"/"Forbidden").</param>
    /// <param name="races">The races to list.</param>
    /// <param name="reportStr">Receives the formatted report line, or empty when there are no races.</param>
    /// <returns><c>true</c> if any races were listed; otherwise <c>false</c>.</returns>
    public bool GetRaceLogString(string allowStatus, IEnumerable<FormKey> races, out string reportStr)
    {
        reportStr = "";
        if (races.Any())
        {
            List<string> dispStrs = new();
            foreach (var raceFK in races)
            {
                string dispStr = raceFK.ToString();
                if (_environmentProvider.LinkCache.TryResolve<IRaceGetter>(raceFK, out var raceGetter) && raceGetter != null && raceGetter.EditorID != null)
                {
                    dispStr = raceGetter.EditorID.ToString();
                }
                dispStrs.Add(dispStr);
            }
            reportStr = allowStatus + " Races: " + string.Join(", ", dispStrs);
            return true;
        }
        return false;
    }

    /// <summary>Builds a one-line "&lt;status&gt; Race Groupings: ..." report from the selected race-grouping checkboxes, if any.</summary>
    /// <param name="allowStatus">Prefix describing the rule.</param>
    /// <param name="raceGroupings">The checkbox list of race groupings.</param>
    /// <param name="reportStr">Receives the formatted report line, or empty when none are selected.</param>
    /// <returns><c>true</c> if any groupings were listed; otherwise <c>false</c>.</returns>
    public bool GetRaceGroupingLogString(string allowStatus, VM_RaceGroupingCheckboxList raceGroupings, out string reportStr)
    {
        reportStr = "";
        var selectedGroupings = raceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label).ToArray();
        if (selectedGroupings.Any())
        {
            reportStr = allowStatus + " Race Groupings: " + string.Join(", ", selectedGroupings);
            return true;
        }
        return false;
    }
    /// <summary>Builds a one-line "&lt;status&gt; Race Groupings: ..." report from a set of grouping labels, if any.</summary>
    /// <param name="allowStatus">Prefix describing the rule.</param>
    /// <param name="raceGroupings">The grouping labels.</param>
    /// <param name="reportStr">Receives the formatted report line, or empty when the set is empty.</param>
    /// <returns><c>true</c> if any groupings were listed; otherwise <c>false</c>.</returns>
    public bool GetRaceGroupingLogString(string allowStatus, HashSet<string> raceGroupings, out string reportStr)
    {
        reportStr = "";
        if (raceGroupings.Any())
        {
            reportStr = allowStatus + " Race Groupings: " + string.Join(", ", raceGroupings);
            return true;
        }
        return false;
    }

    /// <summary>Builds a one-line "&lt;status&gt; Attributes: ..." report from a collection of attribute view models, if any.</summary>
    /// <param name="allowStatus">Prefix describing the rule.</param>
    /// <param name="attributes">The attribute view models (converted to models for formatting).</param>
    /// <param name="reportStr">Receives the formatted report line, or empty when there are none.</param>
    /// <returns><c>true</c> if any attributes were listed; otherwise <c>false</c>.</returns>
    public bool GetAttributeLogString(string allowStatus, ObservableCollection<VM_NPCAttribute> attributes, out string reportStr)
    {
        reportStr = "";
        if (attributes.Any())
        {
            List<string> attributeStrs = new();
            var models = VM_NPCAttribute.DumpViewModelsToModels(attributes);
            var attributeLogs = models.Select(x => x.ToLogString(true, _environmentProvider.LinkCache)).ToArray();
            reportStr = allowStatus + " Attributes: " + string.Join(", ", attributeLogs);
            return true;
        }
        return false;
    }
    /// <summary>Builds a one-line "&lt;status&gt; Attributes: ..." report from a set of attribute models, if any.</summary>
    /// <param name="allowStatus">Prefix describing the rule.</param>
    /// <param name="attributes">The attribute models.</param>
    /// <param name="reportStr">Receives the formatted report line, or empty when there are none.</param>
    /// <returns><c>true</c> if any attributes were listed; otherwise <c>false</c>.</returns>
    public bool GetAttributeLogString(string allowStatus, HashSet<NPCAttribute> attributes, out string reportStr)
    {
        reportStr = "";
        if (attributes.Any())
        {
            var attributeLogs = attributes.Select(x => x.ToLogString(true, _environmentProvider.LinkCache)).ToArray();
            reportStr = allowStatus + " Attributes: " + string.Join(", ", attributeLogs);
            return true;
        }
        return false;
    }

    /// <summary>Formats any major record as a readable string: its name, and (optionally) a qualifier with EditorID and FormKey.</summary>
    /// <param name="getter">The record to describe; may be null.</param>
    /// <param name="fullyQualified">When <c>true</c>, always include the EditorID/FormKey qualifier even if the record has a name.</param>
    /// <returns>"NULL" for a null record; otherwise a name and/or "EditorID | FormKey" string.</returns>
    public static string GetFormLogString(IMajorRecordGetter getter, bool fullyQualified = false)
    {
        if (getter == null)
        {
            return "NULL";
        }
        
        string str = string.Empty;

        if (getter is INamedGetter named && named.Name != null)
        {
            str += named.Name;
            if (!fullyQualified)
            {
                return str;
            }
        }

        string qual = string.Empty;
        
        if (getter.EditorID != null)
        {
            qual += getter.EditorID + " | ";
        }

        qual += getter.FormKey.ToString();

        if (str != string.Empty)
        {
            str += " (" + qual + ")";
        }
        else
        {
            str += qual;
        }
        
        return str;
    }
}

/// <summary>Severity of a logged status message.</summary>
public enum ErrorType
{
    /// <summary>Non-fatal; shown in the warning color.</summary>
    Warning,
    /// <summary>Fatal/important; shown in the error color and raises <see cref="Logger.LoggedError"/>.</summary>
    Error
}