using System.Windows.Controls;

namespace CharacterViewer.Rendering.Views;

/// <summary>
/// Reusable render-pipeline controls panel for any host that drives the
/// renderer via <see cref="VM_CharacterViewer"/>. Bind
/// <see cref="System.Windows.FrameworkElement.DataContext"/> to a viewer VM
/// and the panel surfaces toggles + tunables that affect the rendered output:
/// tone-mapping, shadows, SSAO (+ tunables), eye catch-light, SSS strength,
/// vignette radius / intensity, and the missing-texture wireframe fallback.
///
/// <para>Two-way bindings push edits straight into VM_CharacterViewer
/// properties; the WhenAnyValue subscriptions in the VM constructor forward
/// them to GlRenderer on the next frame, so changes apply immediately
/// without a reload click. The one exception is
/// <see cref="VM_CharacterViewer.RenderMissingTextureAsWireframe"/> - it's
/// consumed at mesh-upload time, so the VM raises
/// <see cref="VM_CharacterViewer.ReloadRequested"/> when it changes; hosts
/// subscribe and re-load the current NPC.</para>
///
/// <para>Mirrors <see cref="UC_CharacterViewerLightingPanel"/>'s shape so the
/// two panels can sit side-by-side in a host UI without visual mismatch.</para>
/// </summary>
public partial class UC_CharacterViewerRenderPanel : UserControl
{
    public UC_CharacterViewerRenderPanel()
    {
        InitializeComponent();
    }
}
