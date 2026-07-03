using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SynthEBD;

/// <summary>
/// Attached property that gives an element a rich 3-part tooltip by key:
/// <c>local:DocTooltip.Key="AssetPatching.FacePatchingMode"</c> looks the key up in
/// <see cref="UiDocs"/>, assigns the <see cref="UiDocEntry"/> as the element's ToolTip (rendered
/// by the implicit UiDocEntry DataTemplate), and wires the global show/hide gate
/// (<see cref="TooltipController.DisplayToolTips"/>) plus a long show duration. The Key value may
/// itself be a Binding (used by the Dashboard's shared tile template). A missing key asserts in
/// debug builds and leaves the element without a tooltip.
/// </summary>
public static class DocTooltip
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(DocTooltip), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject obj) => (string?)obj.GetValue(KeyProperty);
    public static void SetKey(DependencyObject obj, string? value) => obj.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var key = e.NewValue as string;
        if (string.IsNullOrEmpty(key))
        {
            element.ToolTip = null;
            return;
        }

        if (!UiDocs.All.TryGetValue(key, out var entry))
        {
            Debug.WriteLine("DocTooltip: no UiDocs entry registered for key \"" + key + "\"");
            Debug.Assert(false, "DocTooltip: no UiDocs entry registered for key \"" + key + "\"");
            return;
        }

        element.ToolTip = entry;
        ToolTipService.SetShowDuration(element, 60000);
        BindingOperations.SetBinding(element, ToolTipService.IsEnabledProperty,
            new Binding(nameof(TooltipController.DisplayToolTips)) { Source = TooltipController.Instance });
    }
}
