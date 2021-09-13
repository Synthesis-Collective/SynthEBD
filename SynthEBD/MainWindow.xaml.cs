using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SynthEBD
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            //https://stackoverflow.com/questions/25426930/how-can-i-set-wpf-window-size-is-25-percent-of-relative-monitor-screen
            this.Height = (System.Windows.SystemParameters.MaximizedPrimaryScreenHeight);
            this.Width = (System.Windows.SystemParameters.MaximizedPrimaryScreenWidth * 0.5);

            this.DataContext = new MainWindow_ViewModel(); // attach ViewModel to View
        }
    }

}
