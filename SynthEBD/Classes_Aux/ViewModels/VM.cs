using System.ComponentModel;
using System.Reactive.Disposables;
using Noggog;

namespace SynthEBD;

/// <summary>
/// Base class for all SynthEBD view models. Implements <see cref="INotifyPropertyChanged"/> (auto-woven by
/// PropertyChanged.Fody) and <see cref="IDisposableDropoff"/>, owning a <see cref="CompositeDisposable"/>
/// that reactive subscriptions register against and that is torn down on <see cref="Dispose"/>.
/// </summary>
public class VM : INotifyPropertyChanged, IDisposableDropoff
{
    private readonly CompositeDisposable _compositeDisposable = new();
    public event PropertyChangedEventHandler PropertyChanged;

    // Virtual so subclasses owning unmanaged/GL resources (e.g. VM_CharacterViewer)
    // can release them in addition to tearing down reactive subscriptions.
    /// <summary>Tears down all reactive subscriptions registered via <see cref="Add"/>. Virtual so subclasses can also release unmanaged/GL resources.</summary>
    public virtual void Dispose() => _compositeDisposable.Dispose();

    /// <summary>Manually raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.
    /// PropertyChanged.Fody only auto-notifies on this object's own auto-property setters, so a computed
    /// or delegating property whose backing data lives on another object (e.g. a row VM's dropdown list
    /// sourced from its parent's collection) has to be signalled by hand when that external source
    /// changes. The non-conventional name keeps Fody from adopting this as its weave-time event invoker.</summary>
    public void ManuallyRaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Registers a disposable (typically a reactive subscription) to be disposed when this VM is disposed.</summary>
    /// <param name="disposable">The disposable to track.</param>
    public void Add(IDisposable disposable)
    {
        _compositeDisposable.Add(disposable);
    }
}