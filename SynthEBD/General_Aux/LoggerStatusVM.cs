using System.Reactive;
using System.Reactive.Subjects;
using System.Windows.Media;
using System.Collections.ObjectModel;

namespace SynthEBD;

/// <summary>
/// Reactive view model for the run-status line and the on-screen log: the colored status text (plus a
/// backup pair for timed notifications), the bound log-event collection, and the family of status-update /
/// archive / timed-notification methods. Extracted from <see cref="Logger"/> (R12); <see cref="Logger"/>
/// owns one instance (exposed as <see cref="Logger.Status"/>) and forwards its status/log methods to it.
/// Consumers that need live updates (e.g. the status bar) observe this object directly.
/// </summary>
public sealed class LoggerStatusVM : VM
{
    private readonly IEnvironmentStateProvider _environmentProvider;

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

    /// <summary>Constructs the status VM and initializes the status line to the ready state.</summary>
    /// <param name="environmentProvider">Supplies the logger mode (on-screen vs. console).</param>
    public LoggerStatusVM(IEnvironmentStateProvider environmentProvider)
    {
        _environmentProvider = environmentProvider;
        StatusColor = ReadyColor;
        StatusString = ReadyString;
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
}
