namespace SynthEBD;

/// <summary>
/// One control's three-part documentation, rendered as a rich tooltip by the implicit
/// UiDocEntry DataTemplate (App.xaml): a plain-language explanation, a technical explanation for
/// experienced modders/coders, and the motivation for the feature's existence. Attached to
/// elements via <see cref="DocTooltip"/> keys registered in <see cref="UiDocs"/>.
/// </summary>
public sealed record UiDocEntry(string Layperson, string Technical, string Motivation);
