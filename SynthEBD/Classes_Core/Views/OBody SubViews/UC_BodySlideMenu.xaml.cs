using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SynthEBD;

/// <summary>
/// Code-behind for the BodySlide menu user control.
/// </summary>
public partial class UC_BodySlideMenu : UserControl
{
    /// <summary>Initializes the view's XAML components.</summary>
    public UC_BodySlideMenu()
    {
        InitializeComponent();
    }
    /// <summary>Selects the clicked TextBlock's data item in its parent ListBox on left mouse-down,
    /// so clicking the label text (not just the row) selects the item.</summary>
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

    /// <summary>Walks up the visual tree from <paramref name="child"/> to find the nearest
    /// ancestor of type <typeparamref name="T"/>, or null if none exists.</summary>
    private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);

        if (parentObject == null)
            return null;

        T parent = parentObject as T;
        return parent ?? FindVisualParent<T>(parentObject);
    }
}