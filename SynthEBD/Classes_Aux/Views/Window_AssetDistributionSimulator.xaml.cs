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
using System.Windows.Shapes;

namespace SynthEBD
{
    /// <summary>
    /// Code-behind for the asset distribution simulator window. The only logic is constraining
    /// numeric fields to numeric input.
    /// </summary>
    public partial class Window_AssetDistributionSimulator : Window
    {
        public Window_AssetDistributionSimulator()
        {
            InitializeComponent();
        }
    }
}
