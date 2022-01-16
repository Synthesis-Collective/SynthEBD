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
using System.Diagnostics;

namespace SynthEBD
{
    /// <summary>
    /// Interaction logic for UC_DownloadCoordinator.xaml
    /// </summary>
    public partial class UC_DownloadCoordinator : UserControl
    {
        public UC_DownloadCoordinator()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) //https://stackoverflow.com/questions/10238694/example-using-hyperlink-in-wpf
        {
            Hyperlink hl = (Hyperlink)sender;
            string navigateUri = hl.NavigateUri.ToString();
            var sInfo = new System.Diagnostics.ProcessStartInfo(navigateUri)
            {
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(sInfo);
            e.Handled = true;
        }
    }
}
