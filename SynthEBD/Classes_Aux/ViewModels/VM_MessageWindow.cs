using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SynthEBD;

public class MessageWindow
{
    public static void DisplayNotificationOK(string header, string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowOK(header, text);
            box.Show();
        });
    }

    public static void DisplayNotificationOK(string header, ICollection<string> text, string separator)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowOK(header, string.Join(separator, text));
            box.Show();
        });
    }

    public static bool DisplayNotificationYesNo(string header, string text)
    {
        bool result = false;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var box = new VM_MessageWindowYesNo(header, text);
            box.Show();
            result = box.Result;
        });
        return result;
    }

    public static bool DisplayNotificationYesNo(string header, ICollection<string> text, string separator)
    {
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

public class VM_MessageWindowOK : VM
{
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

public class VM_MessageWindowYesNo : VM
{
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