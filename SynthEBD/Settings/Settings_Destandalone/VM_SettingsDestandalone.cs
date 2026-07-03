namespace SynthEBD;

/// <summary>
/// Facade view model giving Destandalone patching ("Force Vanilla Body Mesh Paths") its own
/// navigation page. The underlying settings deliberately remain on
/// <see cref="VM_SettingsTexMesh"/> / <c>Settings_TexMesh</c> (moving them would break existing
/// TexMeshSettings.json files); this type exists so the main window's type-routed
/// ContentPresenter can display the feature as its own modality. Registered as a singleton in
/// <see cref="MainModule"/>.
/// </summary>
public class VM_SettingsDestandalone : VM
{
    public VM_SettingsDestandalone(VM_SettingsTexMesh texMesh)
    {
        TexMesh = texMesh;
    }

    public VM_SettingsTexMesh TexMesh { get; }
}
