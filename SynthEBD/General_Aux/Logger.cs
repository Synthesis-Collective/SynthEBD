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
    private readonly NpcReportBuilder _reportBuilder;

    private List<string> _startupLog = new();
    private Dictionary<string, System.Diagnostics.Stopwatch> _startupTimers = new();
    private int _startupLogIndentCount = 0;
    /// <summary>The reactive status line and on-screen log (extracted in R12); observe this object directly for live updates.</summary>
    public LoggerStatusVM Status { get; }

    // Forwarder properties returning Status's own instances so existing consumers (e.g. VM_LogDisplay) bind unchanged.
    /// <inheritdoc cref="LoggerStatusVM.LoggedEvents"/>
    public ObservableCollection<string> LoggedEvents => Status.LoggedEvents;
    /// <inheritdoc cref="LoggerStatusVM.LogString"/>
    public string LogString => Status.LogString;
    /// <inheritdoc cref="LoggerStatusVM.LoggedError"/>
    public IObservable<Unit> LoggedError => Status.LoggedError;

    public DateTime PatcherExecutionStart { get; set; }

    System.Windows.Threading.DispatcherTimer UpdateTimer { get; set; } = new();
    System.Diagnostics.Stopwatch EllapsedTimer { get; set; } = new();

    public NPCInfo CurrentNPCInfo { get; set; } = null;

    /// <summary>Constructs the logger and initializes the status line to the ready state.</summary>
    /// <param name="patcherIO">File-writing helper used to persist logs and reports.</param>
    /// <param name="environmentProvider">Supplies the logger mode, link cache, and startup log.</param>
    /// <param name="paths">Resolved output paths (used for the log folder).</param>
    public Logger(PatcherIO patcherIO, IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths)
    {
        _patcherIO = patcherIO;
        _environmentProvider = environmentProvider;
        _paths = paths;
        Status = new LoggerStatusVM(environmentProvider);
        _reportBuilder = new NpcReportBuilder(environmentProvider, paths, this);
    }

    // Status line and on-screen log live in LoggerStatusVM (R12); these thin instance forwarders
    // preserve the historic _logger.X(...) call sites.
    /// <inheritdoc cref="LoggerStatusVM.LogMessage(string)"/>
    public void LogMessage(string message) => Status.LogMessage(message);
    /// <inheritdoc cref="LoggerStatusVM.LogMessage(IEnumerable{string})"/>
    public void LogMessage(IEnumerable<string> messages) => Status.LogMessage(messages);
    /// <inheritdoc cref="LoggerStatusVM.Clear"/>
    public void Clear() => Status.Clear();
    /// <inheritdoc cref="LoggerStatusVM.SetSynthesisStartupString"/>
    public void SetSynthesisStartupString() => Status.SetSynthesisStartupString();

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

    // Static formatters live in LogFormatting (R12); these thin forwarders preserve the historic
    // Logger.X(...) call sites. New code may call LogFormatting directly.
    /// <inheritdoc cref="LogFormatting.FormatTimeStamp"/>
    public static string FormatTimeStamp(DateTime dt) => LogFormatting.FormatTimeStamp(dt);
    /// <inheritdoc cref="LogFormatting.DateTimeToHMS"/>
    public static string DateTimeToHMS(DateTime dt) => LogFormatting.DateTimeToHMS(dt);

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

    // Per-NPC verbose-report system lives in NpcReportBuilder (R12); these thin instance forwarders
    // preserve the historic _logger.X(...) call sites.
    /// <inheritdoc cref="NpcReportBuilder.TriggerNPCReporting"/>
    public void TriggerNPCReporting(NPCInfo npcInfo) => _reportBuilder.TriggerNPCReporting(npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.TriggerNPCReportingSave"/>
    public void TriggerNPCReportingSave(NPCInfo npcInfo) => _reportBuilder.TriggerNPCReportingSave(npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.InitializeNewReport"/>
    public void InitializeNewReport(NPCInfo npcInfo) => _reportBuilder.InitializeNewReport(npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.OpenReportSubsection"/>
    public void OpenReportSubsection(string header, NPCInfo npcInfo) => _reportBuilder.OpenReportSubsection(header, npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.LogReport"/>
    public void LogReport(string message, bool triggerSave, NPCInfo npcInfo) => _reportBuilder.LogReport(message, triggerSave, npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.CloseReportSubsection"/>
    public void CloseReportSubsection(NPCInfo npcInfo) => _reportBuilder.CloseReportSubsection(npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.CloseReportSubsectionsTo"/>
    public void CloseReportSubsectionsTo(string label, NPCInfo npcInfo) => _reportBuilder.CloseReportSubsectionsTo(label, npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.CloseReportSubsectionsToParentOf"/>
    public void CloseReportSubsectionsToParentOf(string label, NPCInfo npcInfo) => _reportBuilder.CloseReportSubsectionsToParentOf(label, npcInfo);
    /// <inheritdoc cref="NpcReportBuilder.SaveReport"/>
    public (string, string) SaveReport(NPCInfo npcInfo) => _reportBuilder.SaveReport(npcInfo);

    /// <inheritdoc cref="LogFormatting.FormatLogStringIndents"/>
    public static string FormatLogStringIndents(string s) => LogFormatting.FormatLogStringIndents(s);

    /// <inheritdoc cref="LoggerStatusVM.LogError"/>
    public void LogError(string error) => Status.LogError(error);
    /// <inheritdoc cref="LogFormatting.SpreadFlattenedAssetPack"/>
    public static string SpreadFlattenedAssetPack(FlattenedAssetPack ap, int index, bool indentAtIndex) => LogFormatting.SpreadFlattenedAssetPack(ap, index, indentAtIndex);

    /// <inheritdoc cref="LoggerStatusVM.LogErrorWithStatusUpdate"/>
    public void LogErrorWithStatusUpdate(string error, ErrorType type) => Status.LogErrorWithStatusUpdate(error, type);
    /// <inheritdoc cref="LoggerStatusVM.UpdateStatus(string, bool)"/>
    public void UpdateStatus(string message, bool triggerWarning) => Status.UpdateStatus(message, triggerWarning);
    /// <inheritdoc cref="LoggerStatusVM.UpdateStatus(string, SolidColorBrush)"/>
    public void UpdateStatus(string message, SolidColorBrush newColor) => Status.UpdateStatus(message, newColor);
    /// <inheritdoc cref="LoggerStatusVM.ArchiveStatus"/>
    public void ArchiveStatus() => Status.ArchiveStatus();
    /// <inheritdoc cref="LoggerStatusVM.UnarchiveStatus"/>
    public void UnarchiveStatus() => Status.UnarchiveStatus();
    /// <inheritdoc cref="LoggerStatusVM.CallTimedLogErrorWithStatusUpdateAsync"/>
    public void CallTimedLogErrorWithStatusUpdateAsync(string error, ErrorType type, int durationSec) => Status.CallTimedLogErrorWithStatusUpdateAsync(error, type, durationSec);
    /// <inheritdoc cref="LoggerStatusVM.CallTimedNotifyStatusUpdateAsync(string, int)"/>
    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec) => Status.CallTimedNotifyStatusUpdateAsync(message, durationSec);
    /// <inheritdoc cref="LoggerStatusVM.CallTimedNotifyStatusUpdateAsync(string, int, SolidColorBrush)"/>
    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec, SolidColorBrush textColor) => Status.CallTimedNotifyStatusUpdateAsync(message, durationSec, textColor);
    /// <inheritdoc cref="LoggerStatusVM.ClearStatusError"/>
    public void ClearStatusError() => Status.ClearStatusError();

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

    /// <inheritdoc cref="LogFormatting.GetNPCLogNameString"/>
    public static string GetNPCLogNameString(INpcGetter npc, Logger logger) => LogFormatting.GetNPCLogNameString(npc, logger);

    /// <summary>Instance overload of <see cref="GetNPCLogNameString(INpcGetter, Logger)"/> using this logger.</summary>
    /// <param name="npc">The NPC to describe.</param>
    /// <returns>A pipe-delimited "Name | EditorID | FormKey" identifier.</returns>
    public string GetNPCLogNameString(INpcGetter npc)
    {
        return NameHandler.GetNPCNameSafely(npc, this) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    /// <inheritdoc cref="LogFormatting.GetNPCLogReportingString"/>
    public static string GetNPCLogReportingString(INpcGetter npc) => LogFormatting.GetNPCLogReportingString(npc);

    /// <inheritdoc cref="LogFormatting.GetSubgroupIDString(FlattenedSubgroup)"/>
    public static string GetSubgroupIDString(FlattenedSubgroup subgroup) => LogFormatting.GetSubgroupIDString(subgroup);

    /// <inheritdoc cref="LogFormatting.GetSubgroupIDString(AssetPack.Subgroup)"/>
    public static string GetSubgroupIDString(AssetPack.Subgroup subgroup) => LogFormatting.GetSubgroupIDString(subgroup);

    /// <inheritdoc cref="LogFormatting.GetSubgroupIDString(VM_Subgroup)"/>
    public static string GetSubgroupIDString(VM_Subgroup subgroup) => LogFormatting.GetSubgroupIDString(subgroup);

    /// <inheritdoc cref="LogFormatting.GetSubgroupIDString(VM_SubgroupPlaceHolder)"/>
    public static string GetSubgroupIDString(VM_SubgroupPlaceHolder subgroup) => LogFormatting.GetSubgroupIDString(subgroup);

    /// <inheritdoc cref="LogFormatting.GetBodyShapeDescriptorString(Dictionary{string, HashSet{string}})"/>
    public static string GetBodyShapeDescriptorString(Dictionary<string, HashSet<string>> descriptorList) => LogFormatting.GetBodyShapeDescriptorString(descriptorList);

    /// <inheritdoc cref="LogFormatting.GetBodyShapeDescriptorString{T}(HashSet{T})"/>
    public static string GetBodyShapeDescriptorString<T>(HashSet<T> descriptors)
        where T : BodyShapeDescriptor.LabelSignature
        => LogFormatting.GetBodyShapeDescriptorString(descriptors);

    /// <inheritdoc cref="LogFormatting.GetRaceListLogStrings"/>
    public static string GetRaceListLogStrings(IEnumerable<FormKey> formKeys, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState) => LogFormatting.GetRaceListLogStrings(formKeys, lk, patcherState);

    /// <inheritdoc cref="LogFormatting.TryGetSpecialCaseRaceLogName"/>
    public static bool TryGetSpecialCaseRaceLogName(FormKey raceFormKey, out string name) => LogFormatting.TryGetSpecialCaseRaceLogName(raceFormKey, out name);

    /// <inheritdoc cref="LogFormatting.GetRaceLogString(FormKey, Mutagen.Bethesda.Plugins.Cache.ILinkCache, PatcherState)"/>
    public static string GetRaceLogString(FormKey fk, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState) => LogFormatting.GetRaceLogString(fk, lk, patcherState);

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

    /// <inheritdoc cref="LogFormatting.GetFormLogString"/>
    public static string GetFormLogString(IMajorRecordGetter getter, bool fullyQualified = false) => LogFormatting.GetFormLogString(getter, fullyQualified);
}

/// <summary>Severity of a logged status message.</summary>
public enum ErrorType
{
    /// <summary>Non-fatal; shown in the warning color.</summary>
    Warning,
    /// <summary>Fatal/important; shown in the error color and raises <see cref="Logger.LoggedError"/>.</summary>
    Error
}