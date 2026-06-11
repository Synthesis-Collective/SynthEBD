using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>
/// Attached behavior that restricts a <see cref="TextBox"/> to numeric input. Setting
/// <c>NumericInputBehavior.IsEnabled="True"</c> in XAML subscribes the box's
/// <see cref="UIElement.PreviewTextInput"/> event and rejects any keystroke that would make the field
/// non-numeric, via <see cref="IsNumeric.IsTextNumeric"/>. Replaces the per-view, byte-identical
/// <c>PreviewTextInput="NumericOnly"</c> code-behind handlers (R2).
/// </summary>
public static class NumericInputBehavior
{
    /// <summary>Attached property; set to <c>true</c> on a <see cref="TextBox"/> to restrict it to numeric input.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(NumericInputBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Gets the <see cref="IsEnabledProperty"/> value.</summary>
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    /// <summary>Sets the <see cref="IsEnabledProperty"/> value.</summary>
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            textBox.PreviewTextInput += OnPreviewTextInput;
        }
        else
        {
            textBox.PreviewTextInput -= OnPreviewTextInput;
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        e.Handled = !IsNumeric.IsTextNumeric(textBox, e.Text);
    }
}
