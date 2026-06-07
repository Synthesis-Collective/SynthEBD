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
    /// Code-behind for the Config Path Remapper window.
    /// </summary>
    public partial class Window_ConfigPathRemapper : Window
    {
        /// <summary>Initializes the view's XAML components.</summary>
        public Window_ConfigPathRemapper()
        {
            InitializeComponent();
        }

        /// <summary>Recomputes the main grid's row heights when an expander is expanded.</summary>
        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            UpdateRowHeights();
        }

        /// <summary>Recomputes the main grid's row heights when an expander is collapsed.</summary>
        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            UpdateRowHeights();
        }

        /// <summary>
        /// Sizes each expander's grid row: zero height when hidden, star height when
        /// expanded, and auto height when collapsed.
        /// </summary>
        private void UpdateRowHeights()
        {
            int row = 4;
            foreach (Expander expander in MainGrid.Children.OfType<Expander>())
            {
                if (!expander.IsVisible)
                {
                    MainGrid.RowDefinitions[row].Height = new GridLength(0, GridUnitType.Pixel);
                }
                else if (expander.IsExpanded)
                {
                    MainGrid.RowDefinitions[row].Height = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    MainGrid.RowDefinitions[row].Height = new GridLength(1, GridUnitType.Auto);
                }
                row++;
            }
        }
    }
}
