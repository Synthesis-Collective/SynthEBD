using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SkyRender.Character;

/// <summary>
/// Base view-model for the CharacterViewer rendering tier. Implements
/// <see cref="INotifyPropertyChanged"/> with the classic
/// <c>OnPropertyChanged</c> invoker shape so PropertyChanged.Fody picks
/// it up and weaves every auto-property setter to raise INPC.
///
/// The WhenAnyValue subscriptions in <see cref="VM_CharacterViewer"/> rely
/// on the woven behaviour (e.g. <c>ShowWireframe</c>, <c>ShowLightControls</c>,
/// every per-light intensity / azimuth / elevation property).
/// <see cref="ReactiveUI.ReactiveObject"/>'s differently-named
/// <c>RaisePropertyChanged</c> invoker is not detected by PropertyChanged.Fody,
/// so deriving from it would leave auto-property setters silently not firing
/// INPC and the WhenAnyValue subscriptions would never fire.
/// </summary>
public abstract class ViewerVm : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Standard invoker name PropertyChanged.Fody looks for. Subclass
    /// auto-properties get woven to call this from their setters.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Subclasses override to add their own teardown. Base impl is
    /// a no-op; <see cref="VM_CharacterViewer"/>, for example, disposes its
    /// composite disposable + GL resources here.</summary>
    public virtual void Dispose()
    {
    }
}
