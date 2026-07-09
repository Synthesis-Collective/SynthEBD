using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD;

/// <summary>Static helpers for showing modal OK and Yes/No message dialogs on the UI thread.</summary>
public class MessageWindow
{
    /// <summary>
    /// When true, no dialog is ever shown: OK notifications are swallowed and Yes/No prompts
    /// answer No (the conservative choice), with every suppressed dialog recorded for later
    /// retrieval via <see cref="DrainSuppressedDialogs"/>. Set by automated hosts (the CLI
    /// ui-screenshot harness) where a modal dialog would hang the run forever. Defaults to
    /// false so the GUI is unaffected.
    /// </summary>
    public static bool SuppressAllDialogs { get; set; } = false;

    /// <summary>Dialogs swallowed while <see cref="SuppressAllDialogs"/> was on; guarded by its own lock (dialogs can be requested from any thread).</summary>
    private static readonly List<string> _suppressedDialogs = new();

    /// <summary>Returns and clears the record of suppressed dialogs, so automated hosts can surface them in their own logs.</summary>
    public static List<string> DrainSuppressedDialogs()
    {
        lock (_suppressedDialogs)
        {
            var drained = new List<string>(_suppressedDialogs);
            _suppressedDialogs.Clear();
            return drained;
        }
    }

    /// <summary>Records a suppressed dialog and returns the Yes/No answer the caller should report (always false = No).</summary>
    private static bool RecordSuppressed(string kind, string header, string text)
    {
        lock (_suppressedDialogs)
        {
            _suppressedDialogs.Add("[" + kind + "] " + header + ": " + text);
        }
        return false;
    }

    /// <summary>Shows a modal OK dialog with the given header and text.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body text.</param>
    public static void DisplayNotificationOK(string header, string text)
    {
        if (SuppressAllDialogs) { RecordSuppressed("OK", header, text); return; }
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowOK(header, text);
            box.Show();
        });
    }

    /// <summary>Shows a modal OK dialog whose body joins the given lines with a separator.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body lines.</param>
    /// <param name="separator">Separator joining the lines.</param>
    public static void DisplayNotificationOK(string header, ICollection<string> text, string separator)
    {
        if (SuppressAllDialogs) { RecordSuppressed("OK", header, string.Join(separator, text)); return; }
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowOK(header, string.Join(separator, text));
            box.Show();
        });
    }

    /// <summary>Shows a modal Yes/No dialog and returns the user's choice.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body text.</param>
    /// <returns><c>true</c> if the user chose Yes.</returns>
    public static bool DisplayNotificationYesNo(string header, string text)
    {
        if (SuppressAllDialogs) { return RecordSuppressed("YesNo->No", header, text); }
        bool result = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowYesNo(header, text);
            box.Show();
            result = box.Result;
        });
        return result;
    }

    /// <summary>Shows a modal Yes/No dialog whose body joins the given lines, and returns the user's choice.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body lines.</param>
    /// <param name="separator">Separator joining the lines.</param>
    /// <returns><c>true</c> if the user chose Yes.</returns>
    public static bool DisplayNotificationYesNo(string header, ICollection<string> text, string separator)
    {
        if (SuppressAllDialogs) { return RecordSuppressed("YesNo->No", header, string.Join(separator, text)); }
        bool result = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowYesNo(header, string.Join(separator, text));
            box.Show();
            result = box.Result;
        });
        return result;
    }
}

/// <summary>View model for the OK message dialog (header, text, plus OK and copy-to-clipboard commands).</summary>
public class VM_MessageWindowOK : VM
{
    /// <summary>Builds the dialog VM and its window; rethrows with context if construction fails.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body text.</param>
    public VM_MessageWindowOK(string header, string text)
    {
        try
        {
            Header = header;
            Text = text;
            _window = new();

            OkCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    _window.Close();
                });

            CopyTextCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    Clipboard.SetText(Text);
                });
        }
        catch (Exception e)
        {
            var outerMessage = "SynthEBD crashed while trying to generate a popup. The message of the popup is as follows:\n" + text + "\nThe full stack trace is as follows";
            var messageException = new Exception(outerMessage, e);
            throw messageException;
        }
    }

    public string Header { get; set; }
    public string Text { get; set; }
    private Window_MessageWindowOK _window { get; }
    public RelayCommand OkCommand { get; }
    public RelayCommand CopyTextCommand { get; }

    /// <summary>Shows the dialog modally on the UI thread.</summary>
    public void Show()
    {
        // Ensuring the code runs on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window.DataContext = this;
            _window.ShowDialog();
        });
    }
}

/// <summary>View model for the Yes/No message dialog (header, text, Yes/No and copy commands, and the chosen <see cref="Result"/>).</summary>
public class VM_MessageWindowYesNo : VM
{
    /// <summary>Builds the dialog VM and its window; rethrows with context if construction fails.</summary>
    /// <param name="header">Dialog title.</param>
    /// <param name="text">Body text.</param>
    public VM_MessageWindowYesNo(string header, string text)
    {
        try
        {
            Header = header;
            Text = text;
            _window = new();

            YesCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    Result = true;
                    _window.Close();
                });

            NoCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    Result = false;
                    _window.Close();
                });

            CopyTextCommand = new RelayCommand(
                canExecute: _ => true,
                execute: _ =>
                {
                    Clipboard.SetText(Text);
                });
        }
        catch (Exception e)
        {
            var outerMessage = "SynthEBD crashed while trying to generate a popup. The message of the popup is as follows:\n" + text + "\nThe full stack trace is as follows";
            var messageException = new Exception(outerMessage, e);
            throw messageException;
        }
    }

    public string Header { get; set; }
    public string Text { get; set; }
    public bool Result { get; set; }
    private Window_MessageWindowYesNo _window { get; }
    public RelayCommand YesCommand { get; }
    public RelayCommand NoCommand { get; }
    public RelayCommand CopyTextCommand { get; }

    /// <summary>Shows the dialog modally on the UI thread.</summary>
    public void Show()
    {
        // Ensuring the code runs on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window.DataContext = this;
            _window.ShowDialog();
        });
    }
}