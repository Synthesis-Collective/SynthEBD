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

namespace SynthEBD;

public enum LogMode
{
    SynthEBD,
    Synthesis
}
public sealed class Logger : VM
{
    private readonly DisplayedItemVm _displayedItemVm;
    private readonly VM_LogDisplay _logDisplay;
    private readonly IEnvironmentStateProvider _environmentProvider;
    private readonly PatcherIO _patcherIO;
    private SynthEBDPaths _paths;

    public string StatusString { get; set; }
    public string BackupStatusString { get; set; }
    public ObservableCollection<string> LoggedEvents { get; set; } = new();
    public string LogString => string.Join(Environment.NewLine, LoggedEvents);
    public SolidColorBrush StatusColor { get; set; }
    public SolidColorBrush BackupStatusColor { get; set; }

    public SolidColorBrush ReadyColor = new SolidColorBrush(Colors.Green);
    public SolidColorBrush WarningColor = new SolidColorBrush(Colors.Yellow);
    public SolidColorBrush ErrorColor = new SolidColorBrush(Colors.Red);
    public string ReadyString = "Ready To Patch";

    private readonly Subject<Unit> _loggedError = new();
    public IObservable<Unit> LoggedError => _loggedError;

    public DateTime PatcherExecutionStart { get; set; }

    System.Windows.Threading.DispatcherTimer UpdateTimer { get; set; } = new();
    System.Diagnostics.Stopwatch EllapsedTimer { get; set; } = new();

    public class NPCReport
    {
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

    public Logger(PatcherIO patcherIO, IEnvironmentStateProvider environmentProvider, SynthEBDPaths paths)
    {
        StatusColor = ReadyColor;
        StatusString = ReadyString;
        _patcherIO = patcherIO;
        _environmentProvider = environmentProvider;
        _paths = paths;
    }

    public void LogMessage(string message)
    {
        switch (_environmentProvider.LoggerMode)
        {
            case LogMode.SynthEBD: LoggedEvents.Add(message); break;
            case LogMode.Synthesis: Console.WriteLine(message); break;
        }
    }

    public void LogMessage(IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            LogMessage(message);
        }
    }

    public void Clear()
    {
        LoggedEvents.Clear();
    }

    public void TriggerNPCReporting(NPCInfo npcInfo)
    {
        npcInfo.Report.LogCurrentNPC = true;
    }

    public void TriggerNPCReportingSave(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            npcInfo.Report.SaveCurrentNPCLog = true;
        }
    }

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

    public void CloseReportSubsection(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC)
        {
            npcInfo.Report.CurrentElement = npcInfo.Report.ReportElementHierarchy[npcInfo.Report.CurrentElement];
        }
    }

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

    public void SaveReport(NPCInfo npcInfo)
    {
        if (npcInfo.Report.LogCurrentNPC && npcInfo.Report.SaveCurrentNPCLog)
        {
            string saveName = IO_Aux.MakeValidFileName(npcInfo.Report.NameString + ".xml");
            string outputFile = System.IO.Path.Combine(_paths.LogFolderPath, PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), saveName);

            XDocument output = new XDocument();
            output.Add(npcInfo.Report.RootElement);

            Task.Run(() => PatcherIO.WriteTextFile(outputFile, FormatLogStringIndents(output.ToString()), this));
        }
    }

    private static string FormatLogStringIndents(string s)
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
            else if (split[i].Trim().StartsWith('<'))
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

    private static string Indent(string s, int count)
    {
        for (int i = 0; i < count; i++)
        {
            s = "\t" + s;
        }
        return s;
    }

    private class Utf8StringWriter : System.IO.StringWriter
    {
        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }
    }

    public void LogError(string error)
    {
        switch (_environmentProvider.LoggerMode)
        {
            case LogMode.SynthEBD: LoggedEvents.Add(error); break;
            case LogMode.Synthesis: Console.WriteLine(error); break;
        }
        _loggedError.OnNext(Unit.Default);
    }
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

    public void LogErrorWithStatusUpdate(string error, ErrorType type)
    {
        LoggedEvents.Add(error);
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

    public void UpdateStatus(string message, bool triggerWarning)
    {
        StatusString = message;
        if (triggerWarning)
        {
            StatusColor = WarningColor;
        }
    }

    public void UpdateStatus(string message, SolidColorBrush newColor)
    {
        StatusString = message;
        StatusColor = newColor;
    }

    public async Task UpdateStatusAsync(string message, bool triggerWarning)
    {
        await Task.Run(() => _UpdateStatusAsync(message, triggerWarning));
    }

    private async Task _UpdateStatusAsync(string message, bool triggerWarning)
    {
        StatusString = message;
        if (triggerWarning)
        {
            StatusColor = WarningColor;
        }
    }

    public async Task ArchiveStatusAsync()
    {
        await Task.Run(() => _ArchiveStatusAsync());
    }

    private async Task _ArchiveStatusAsync()
    {
        BackupStatusString = StatusString;
        BackupStatusColor = StatusColor;
    }
    public void ArchiveStatus()
    {
        BackupStatusString = StatusString;
        BackupStatusColor = StatusColor;
    }

    public async Task UnarchiveStatusAsync()
    {
        await Task.Run(() => _DeArchiveStatusAsync());
    }

    private async Task _DeArchiveStatusAsync()
    {
        StatusString = BackupStatusString;
        StatusColor = BackupStatusColor;
    }

    public void UnarchiveStatus()
    {
        StatusString = BackupStatusString;
        StatusColor = BackupStatusColor;
    }

    public void TimedNotifyStatusUpdate(string error, ErrorType type, int durationSec)
    {
        ArchiveStatus();
        LogErrorWithStatusUpdate(error, type);

        var t = Task.Factory.StartNew(() =>
        {
            Task.Delay(durationSec * 1000).Wait();
        });
        t.Wait();
        UnarchiveStatus();
    }

    public void CallTimedLogErrorWithStatusUpdateAsync(string error, ErrorType type, int durationSec)
    {
        Task.Run(() => TimedLogErrorWithStatusUpdateAsync(error, type, durationSec));
    }

    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec)
    {
        Task.Run(() => TimedNotifyStatusUpdateAsync(message, durationSec));
    }

    public void CallTimedNotifyStatusUpdateAsync(string message, int durationSec, SolidColorBrush textColor)
    {
        Task.Run(() => TimedNotifyStatusUpdateAsync(message, durationSec, textColor));
    }

    private async Task TimedLogErrorWithStatusUpdateAsync(string error, ErrorType type, int durationSec)
    {
        ArchiveStatus();
        LogErrorWithStatusUpdate(error, type);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    private async Task TimedNotifyStatusUpdateAsync(string message, int durationSec)
    {
        ArchiveStatus();
        UpdateStatus(message, false);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    private async Task TimedNotifyStatusUpdateAsync(string message, int durationSec, SolidColorBrush textColor)
    {
        ArchiveStatus();
        UpdateStatus(message, textColor);

        // Await the Task to allow the UI thread to render the view
        // in order to show the changes     
        await Task.Delay(durationSec * 1000);

        UnarchiveStatus();
    }

    public void ClearStatusError()
    {
        StatusString = ReadyString;
        StatusColor = ReadyColor;
    }
    public void StartTimer()
    {
        UpdateTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, System.Windows.Application.Current.Dispatcher); // arguments here are forcing the dispatcher to run on the UI thread (otherwise UpdateTimer.Tick fires on a different thread and gets missed by the UI, so the event handler is never called).
        EllapsedTimer = new System.Diagnostics.Stopwatch();
        UpdateTimer.Interval = TimeSpan.FromSeconds(1);
        UpdateTimer.Tick += timer_Tick;
        UpdateTimer.Start();
        EllapsedTimer.Start();
    }

    public void StopTimer()
    {
        EllapsedTimer.Stop();
        UpdateTimer.Stop();
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        UpdateStatus("Patching: " + GetEllapsedTime(), false);
    }

    public string GetEllapsedTime()
    {
        TimeSpan ts = EllapsedTimer.Elapsed;
        return string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds);
    }

    public static string GetNPCLogNameString(INpcGetter npc, Logger logger)
    {
        return NameHandler.GetNPCNameSafely(npc, logger) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    public string GetNPCLogNameString(INpcGetter npc)
    {
        return NameHandler.GetNPCNameSafely(npc, this) + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

public static string GetNPCLogReportingString(INpcGetter npc)
    {
        return IO_Aux.MakeValidFileName(npc.Name?.String + " (" + EditorIDHandler.GetEditorIDSafely(npc) + ") " + npc.FormKey.ToString().Replace(':', '-'));
    }

    public static string GetSubgroupIDString(FlattenedSubgroup subgroup)
    {
        return subgroup.Id + ": " + subgroup.Name;
    }

    public static string GetSubgroupIDString(AssetPack.Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

    public static string GetSubgroupIDString(VM_Subgroup subgroup)
    {
        return subgroup.ID + ": " + subgroup.Name;
    }

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

    public static string GetBodyShapeDescriptorString(HashSet<BodyShapeDescriptor.LabelSignature> descriptors)
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

    public static string GetRaceListLogStrings(IEnumerable<FormKey> formKeys, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        return "[" + String.Join(", ", formKeys.Select(x => GetRaceLogString(x, lk, patcherState))) + "]";
    }

    public static string GetRaceLogString(FormKey fk, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk, PatcherState patcherState)
    {
        if (!patcherState.GeneralSettings.VerboseModeDetailedAttributes)
        {
            return fk.ToString();
        }

        if (lk.TryResolve<IRaceGetter>(fk, out var raceGetter))
        {
            if (raceGetter.Name != null && !raceGetter.Name.ToString().IsNullOrWhitespace())
            {
                return raceGetter.Name.ToString();
            }
            else if (raceGetter.EditorID != null && !raceGetter.EditorID.ToString().IsNullOrWhitespace())
            {
                return raceGetter.EditorID.ToString();
            }
            else
            {
                return "(No Name or EditorID: " + fk.ToString() + ")";
            }
        }
        else
        {
            return "(Not Currently In Load Order)";
        }
    }

    public bool GetRaceLogString(string allowStatus, ObservableCollection<FormKey> races, out string reportStr)
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

    public bool GetRaceGroupingLogString(string allowStatus, VM_RaceGroupingCheckboxList raceGroupings, out string reportStr)
    {
        reportStr = "";
        var selectedGroupings = raceGroupings.RaceGroupingSelections.Where(x => x.IsSelected).Select(x => x.SubscribedMasterRaceGrouping.Label);
        if (selectedGroupings.Any())
        {
            reportStr = allowStatus + " Race Groupings: " + string.Join(", ", selectedGroupings);
            return true;
        }
        return false;
    }

    public bool GetAttributeLogString(string allowStatus, ObservableCollection<VM_NPCAttribute> attributes, out string reportStr)
    {
        reportStr = "";
        if (attributes.Any())
        {
            List<string> attributeStrs = new();
            var models = VM_NPCAttribute.DumpViewModelsToModels(attributes);
            var attributeLogs = models.Select(x => x.ToLogString(true, _environmentProvider.LinkCache));
            reportStr = allowStatus + " Attributes: " + string.Join(", ", attributeLogs);
            return true;
        }
        return false;
    }
}

public enum ErrorType
{
    Warning,
    Error
}