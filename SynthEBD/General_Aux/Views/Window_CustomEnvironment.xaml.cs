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
    /// Code-behind for the custom-environment dialog (the view for <see cref="VM_CustomEnvironment"/>), where
    /// the user selects a game executable to build a valid Mutagen environment.
    /// </summary>
    public partial class Window_CustomEnvironment : Window
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public Window_CustomEnvironment()
        {
            InitializeComponent();
        }
    }
}
