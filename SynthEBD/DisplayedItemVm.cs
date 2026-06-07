namespace SynthEBD;

/// <summary>
/// Holds the currently displayed content view model. Navigation swaps
/// <see cref="DisplayedViewModel"/>, and a type-routed <c>DataTemplate</c> renders the matching
/// view. Registered as a singleton so all navigators share one display slot.
/// </summary>
public class DisplayedItemVm : VM
{
    /// <summary>The view model currently shown in the main content area.</summary>
    public object DisplayedViewModel { get; set; }
}