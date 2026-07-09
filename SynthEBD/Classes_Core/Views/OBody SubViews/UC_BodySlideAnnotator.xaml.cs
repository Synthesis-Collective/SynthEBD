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
    /// Code-behind for the BodySlide annotator user control.
    /// </summary>
    public partial class UC_BodySlideAnnotator : UserControl
    {
        /// <summary>Initializes the view's XAML components and wires the preview rail's first-load priming.</summary>
        public UC_BodySlideAnnotator()
        {
            InitializeComponent();
            // Prime the preview rail (preset list + default NPC) once the view is live —
            // deferred to Loaded/DataContextChanged like UC_BodyTypeProfileEditor so VM
            // construction order doesn't matter. Prime() itself is idempotent.
            Loaded += (_, _) => TryPrime();
            DataContextChanged += (_, _) => { if (IsLoaded) TryPrime(); };
        }

        private void TryPrime()
        {
            if (DataContext is VM_BodySlideAnnotator vm)
            {
                vm.PreviewPanel?.Prime();
            }
        }
    }
}
