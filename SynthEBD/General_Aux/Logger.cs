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

namespace SynthEBD;

public sealed class Logger : VM
{
    private readonly DisplayedItemVm _displayedItemVm;
    private readonly VM_LogDisplay _logDisplay;
    private readonly PatcherEnvironmentProvider _patcherEnvironmentProvider;
    private readonly PatcherIO _patcherIO;
    public string StatusString { get; set; }
    public string BackupStatusString { get; set; }
    public string LogString { get; set; }
    private string _logFolderPath { get; set; } = "";
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
            NameString = GetNPCLogReportingString(npcInfo.NPC);
        }
        public string NameString { get; set; }
        public bool LogCurrentNPC { get; set; } = false;
        public bool SaveCurrentNPCLog { get; set; } = false;
        public System.Xml.Linq.XElement RootElement { get; set; } = null;
        public System.Xml.Linq.XElement CurrentElement { get; set; } = null;
        public Dictionary<System.Xml.Linq.XElement, System.Xml.Linq.XElement> ReportElementHierarchy { get; set; } = new();
        public int CurrentLayer;
    }

    public Logger(PatcherEnvironmentProvider patcherEnvironmentProvider, PatcherIO patcherIO)
    {
        StatusColor = ReadyColor;
        StatusString = ReadyString;
        _patcherEnvironmentProvider = patcherEnvironmentProvider;
        _patcherIO = patcherIO;

        // set default log path
        var assemblyPath = Assembly.GetEntryAssembly().Location;
        if (assemblyPath != null)
        {
            _logFolderPath = Path.Combine(Path.GetDirectoryName(assemblyPath), "Logs");
        }
        
    }

    public void SetLogPath(string path)
    {
        _logFolderPath = path;
    }

    public void LogMessage(string message)
    {
        LogString += message + Environment.NewLine;
    }

    public void LogMessage(IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            LogString += message + Environment.NewLine;
        }
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

            if (_patcherEnvironmentProvider.Environment.LinkCache.TryResolve<IRaceGetter>(npcInfo.AssetsRace, out var assetsRaceGetter))
            {
                LogReport("Assets race: " + EditorIDHandler.GetEditorIDSafely(assetsRaceGetter), false, npcInfo); ;
            }
            if (_patcherEnvironmentProvider.Environment.LinkCache.TryResolve<IRaceGetter>(npcInfo.BodyShapeRace, out var bodyRaceGetter))
            {
                LogReport("Body Shape race: " + EditorIDHandler.GetEditorIDSafely(bodyRaceGetter), false, npcInfo);
            }
            if (_patcherEnvironmentProvider.Environment.LinkCache.TryResolve<IRaceGetter>(npcInfo.HeightRace, out var heightRaceGetter))
            {
                LogReport("Height race: " + EditorIDHandler.GetEditorIDSafely(heightRaceGetter), false, npcInfo);
            }
            if (_patcherEnvironmentProvider.Environment.LinkCache.TryResolve<IRaceGetter>(npcInfo.HeadPartsRace, out var headPartsRaceGetter))
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
        if (npcInfo.Report.LogCurrentNPC)
        {
            if (npcInfo.Report.SaveCurrentNPCLog)
            {
                string outputFile = System.IO.Path.Combine(_logFolderPath, PatcherExecutionStart.ToString("yyyy-MM-dd-HH-mm", System.Globalization.CultureInfo.InvariantCulture), npcInfo.Report.NameString + ".xml");

                XDocument output = new XDocument();
                output.Add(npcInfo.Report.RootElement);

                Task.Run(() => _patcherIO.WriteTextFile(outputFile, FormatLogStringIndents(output.ToString())));
            }
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
        LogString += error + Environment.NewLine;
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
        LogString += error + Environment.NewLine;
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

    public static string GetNPCLogNameString(INpcGetter npc)
    {
        return npc.Name?.String + " | " + EditorIDHandler.GetEditorIDSafely(npc) + " | " + npc.FormKey.ToString();
    }

    public static string GetNPCLogReportingString(INpcGetter npc)
    {
        return npc.Name?.String + " (" + EditorIDHandler.GetEditorIDSafely(npc) + ") " + npc.FormKey.ToString().Replace(':', '-');
    }

    public static string GetSubgroupIDString(FlattenedSubgroup subgroup)
    {
        return subgroup.Id + ": " + subgroup.Name;
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

    public static string GetRaceListLogStrings(IEnumerable<FormKey> formKeys, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk)
    {
        return "[" + String.Join(", ", formKeys.Select(x => GetRaceLogString(x, lk))) + "]";
    }

    public static string GetRaceLogString(FormKey fk, Mutagen.Bethesda.Plugins.Cache.ILinkCache lk)
    {
        if (!PatcherSettings.General.VerboseModeDetailedAttributes)
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
}

public enum ErrorType
{
    Warning,
    Error
}