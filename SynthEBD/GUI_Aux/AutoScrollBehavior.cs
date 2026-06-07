using System.Windows;
using System.Windows.Controls;

namespace SynthEBD;

/// <summary>Attached behavior that keeps a <see cref="ScrollViewer"/> pinned to the bottom as content
/// grows (e.g. a live log view). Enabled via the attached <c>AutoScroll</c> dependency property; it only
/// auto-scrolls when the extent height changes, so the user can still scroll up freely.</summary>
public static class AutoScrollBehavior // https://stackoverflow.com/questions/8370209/how-to-scroll-to-the-bottom-of-a-scrollviewer-automatically-with-xaml-and-bindin
{
    /// <summary>Attached <see cref="bool"/> property; setting it <c>true</c> wires up auto-scrolling.</summary>
    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached("AutoScroll", typeof(bool), typeof(AutoScrollBehavior), new PropertyMetadata(false, AutoScrollPropertyChanged));


    /// <summary>Callback for <see cref="AutoScrollProperty"/>: subscribes to (or unsubscribes from)
    /// <see cref="ScrollViewer.ScrollChanged"/> and scrolls to the end when first enabled.</summary>
    public static void AutoScrollPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var scrollViewer = obj as ScrollViewer;
        if (scrollViewer != null && (bool)args.NewValue)
        {
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            scrollViewer.ScrollToEnd();
        }
        else
        {
            scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        }
    }

    /// <summary>Scrolls to the bottom only when the content's extent height changed, so manual
    /// upward scrolling is preserved.</summary>
    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Only scroll to bottom when the extent changed. Otherwise you can't scroll up
        if (e.ExtentHeightChange != 0)
        {
            var scrollViewer = sender as ScrollViewer;
            scrollViewer?.ScrollToBottom();
        }
    }

    /// <summary>Getter for the attached <see cref="AutoScrollProperty"/>.</summary>
    public static bool GetAutoScroll(DependencyObject obj)
    {
        return (bool)obj.GetValue(AutoScrollProperty);
    }

    /// <summary>Setter for the attached <see cref="AutoScrollProperty"/>.</summary>
    public static void SetAutoScroll(DependencyObject obj, bool value)
    {
        obj.SetValue(AutoScrollProperty, value);
    }
}