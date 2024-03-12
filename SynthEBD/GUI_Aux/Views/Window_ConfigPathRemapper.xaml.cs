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
    /// Interaction logic for Window_ConfigPathRemapper.xaml
    /// </summary>
    public partial class Window_ConfigPathRemapper : Window
    {
        public Window_ConfigPathRemapper()
        {
            InitializeComponent();
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            UpdateRowHeights();
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            UpdateRowHeights();
        }

        private void UpdateRowHeights()
        {
            // Update the height of rows containing expanders
            int row = 3; // Start at row 3
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
