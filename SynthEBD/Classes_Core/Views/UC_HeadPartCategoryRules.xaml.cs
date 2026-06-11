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
    /// Code-behind for the head part category rules user control.
    /// </summary>
    public partial class UC_HeadPartCategoryRules : UserControl
    {
        /// <summary>Initializes the view's XAML components and caps the control's height to
        /// half the primary screen height.</summary>
        public UC_HeadPartCategoryRules()
        {
            InitializeComponent();
            this.MaxHeight = (System.Windows.SystemParameters.PrimaryScreenHeight * 0.5);
        }
    }
}
