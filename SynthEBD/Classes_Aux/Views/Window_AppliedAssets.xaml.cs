using System.Windows;

namespace SynthEBD;

/// <summary>
/// Non-modal viewer for the subgroups currently applied to the render preview and the asset
/// files they contribute. Launched by the "View Current" button in <see cref="UC_AssetPresenter"/>;
/// binds <see cref="VM_AssetPresenter.AppliedSubgroups"/> directly, so it stays live while the
/// user keeps selecting subgroups or rerolling. Single-instance per presenter (managed by the
/// launching command).
/// </summary>
public partial class Window_AppliedAssets : Window
{
    public Window_AppliedAssets(VM_AssetPresenter presenter)
    {
        InitializeComponent();
        DataContext = presenter;
    }
}
