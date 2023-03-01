using BespokeFusion;
using ReactiveUI;
using System.Windows;
using System.Windows.Media;

namespace SynthEBD;

public class CustomMessageBox
{
    private List<CustomMaterialMessageBox> MessageQueue { get; set; } = new();
    private bool IsWindowInitialized { get; set; } = false; // CustomMaterialMessageBox seems to have a bug where closing a message box before the application window initializes closes the application window as well as the message box. Therefore, messages must be delayed until the window loads.
    public CustomMessageBox()
    {
        this.WhenAnyValue(x => x.IsWindowInitialized).Subscribe(x => ShowMessageQueue());
    }

    public void ShowMessageQueue()
    {
        foreach (var message in MessageQueue)
        {
            message.Show();
        }
        MessageQueue.Clear();
    }

    public void AllowMessageDisplay()
    {
        IsWindowInitialized = true;
    }

    public static void DisplayNotificationOK(string title, string text) // be sure caller is calling after main window is shown
    {
        var box = new CustomMaterialMessageBox()
        {
            TxtTitle = { Text = title, Foreground = Brushes.White },
            TxtMessage = { Text = text, Foreground = Brushes.White },
            BtnOk = { Content = "OK" },
            BtnCancel = { IsEnabled = false, Visibility = Visibility.Hidden },
            MainContentControl = { Background = Brushes.Black },
            TitleBackgroundPanel = { Background = Brushes.Black },
            BorderBrush = Brushes.Silver
        };

        box.Show();
    }

    public void DisplayNotificationOK_WindowSafe(string title, string text)
    {
        var box = new CustomMaterialMessageBox()
        {
            TxtTitle = { Text = title, Foreground = Brushes.White },
            TxtMessage = { Text = text, Foreground = Brushes.White },
            BtnOk = { Content = "OK" },
            BtnCancel = { IsEnabled = false, Visibility = Visibility.Hidden },
            MainContentControl = { Background = Brushes.Black },
            TitleBackgroundPanel = { Background = Brushes.Black },
            BorderBrush = Brushes.Silver
        };
        if (IsWindowInitialized)
        {
            box.Show();
        }
        else
        {
            MessageQueue.Add(box);
        }
    }

    public static bool DisplayNotificationYesNo(string title, string text)
    {
        var box = new CustomMaterialMessageBox()
        {
            TxtMessage = { Text = text, Foreground = Brushes.White },
            TxtTitle = { Text = title, Foreground = Brushes.White },
            BtnOk = { Content = "Yes" },
            BtnCancel = { Content = "No" },
            MainContentControl = { Background = Brushes.Black },
            TitleBackgroundPanel = { Background = Brushes.Black },
            BorderBrush = Brushes.Silver
        };
        box.Show();

        return box.Result == MessageBoxResult.OK;
    }

    public void DisplayEvalErrorMessage()
    {
        Application.Current.Dispatcher.Invoke((Action)delegate { // apparently thread-safe to do it this way - https://stackoverflow.com/questions/2329978/the-calling-thread-must-be-sta-because-many-ui-components-require-this
            DisplayNotificationOK_WindowSafe("Eval-Expression License Expired", "SynthEBD's asset distribution functionality depends on a month-to-month license of Eval-Expression.NET, and it appears this license has expired for the current build of SynthEBD. Please check the GitHub or Nexus page to see if an updated version has been released, and install the update if so. Otherwise, please contact Piranha91 or another member of the Synthesis Collective to refresh this license by updating the Eval-Expression NuGet package.");
            MainWindow_ViewModel.EvalMessageTriggered = true;
        });
    }
}