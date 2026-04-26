using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CharacterViewer.Rendering;

/// <summary>
/// Base view-model for the CharacterViewer rendering tier. Implements
/// <see cref="INotifyPropertyChanged"/> with the classic
/// <c>OnPropertyChanged</c> invoker shape so PropertyChanged.Fody picks
/// it up and weaves every auto-property setter to raise INPC.
///
/// SynthEBD's pre-extraction <c>VM</c> base class had this exact shape and
/// the WhenAnyValue subscriptions in <see cref="VM_CharacterViewer"/> rely
/// on the woven behaviour (e.g. <c>ShowWireframe</c>, <c>ShowLightControls</c>,
/// every per-light intensity / azimuth / elevation property). After the
/// move (Phase B2d) the VM briefly inherited <see cref="ReactiveUI.ReactiveObject"/>
/// — which exposes the differently-named <c>RaisePropertyChanged</c> invoker
/// — and PropertyChanged.Fody's default detection didn't recognize it, so
/// auto-property setters silently stopped firing INPC and the subscriptions
/// never observed changes (wireframe / show-lights checkboxes did nothing).
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
