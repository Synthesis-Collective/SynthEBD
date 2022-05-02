using BespokeFusion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SynthEBD
{
    public class CustomMessageBox
    {
        public static void DisplayNotificationOK(string title, string text)
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
    }
}
