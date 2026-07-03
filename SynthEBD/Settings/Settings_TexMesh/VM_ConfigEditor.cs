namespace SynthEBD;

/// <summary>
/// Facade view model giving the asset-pack Config Editor its own navigation page. All state and
/// commands live on the shared <see cref="VM_SettingsTexMesh"/> singleton (the editor and the
/// Asset Patching menu operate on the same packs); this type exists so the main window's
/// type-routed ContentPresenter can display the editor separately. Registered as a singleton in
/// <see cref="MainModule"/>.
/// </summary>
public class VM_ConfigEditor : VM
{
    public VM_ConfigEditor(VM_SettingsTexMesh texMesh)
    {
        TexMesh = texMesh;
    }

    public VM_SettingsTexMesh TexMesh { get; }
}
