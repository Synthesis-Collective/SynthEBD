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
    /// Code-behind for the 7-Zip progress window (the view for <see cref="VM_7ZipInterface"/>), which shows
    /// console-style extraction/listing output.
    /// </summary>
    public partial class Window_7ZipInterface : Window
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public Window_7ZipInterface()
        {
            InitializeComponent();
        }
    }
}
