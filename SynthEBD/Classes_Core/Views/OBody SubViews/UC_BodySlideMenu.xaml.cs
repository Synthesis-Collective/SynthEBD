using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SynthEBD;

/// <summary>
/// Interaction logic for UC_BodySlideMenu.xaml
/// </summary>
public partial class UC_BodySlideMenu : UserControl
{
    public UC_BodySlideMenu()
    {
        InitializeComponent();
    }
    private void TextBlock_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            // Find the parent ListBox
            ListBox listBox = FindVisualParent<ListBox>(textBlock);

            // Handle your logic here
            // You can access the DataContext of the TextBlock, which is your data item
            if (listBox != null)
            {
                listBox.SelectedItem = textBlock.DataContext;
            }
        }
    }

    private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null)
            return null;

        T parent = parentObject as T;
        return parent ?? FindVisualParent<T>(parentObject);
    }
}