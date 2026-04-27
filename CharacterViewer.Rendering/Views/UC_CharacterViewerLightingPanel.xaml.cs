using System.Windows.Controls;

namespace CharacterViewer.Rendering.Views;

/// <summary>
/// Reusable lighting controls panel for any host that drives the renderer
/// via <see cref="VM_CharacterViewer"/>. Bind <see cref="System.Windows.FrameworkElement.DataContext"/>
/// to a viewer VM and the panel takes over: layout / color-scheme dropdowns
/// (with Save / Delete preset commands), ambient slider, per-light enable
/// toggles, Show-Lights gizmo gate, and a per-light editor (intensity /
/// azimuth / elevation / color) gated on
/// <see cref="VM_CharacterViewer.SelectedLightIndex"/>.
///
/// <para>Click-on-arrow picking lives in the host's viewport code-behind
/// (the <c>GLWpfControl</c> is host-specific). A click handler should call
/// <c>VM_CharacterViewer.HitTestLightArrow</c> and assign the result to
/// <see cref="VM_CharacterViewer.SelectedLightIndex"/>; this panel reacts
/// automatically.</para>
/// </summary>
public partial class UC_CharacterViewerLightingPanel : UserControl
{
    public UC_CharacterViewerLightingPanel()
    {
        InitializeComponent();
    }
}
