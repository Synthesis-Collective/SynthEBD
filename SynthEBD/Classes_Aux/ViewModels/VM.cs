using System.ComponentModel;
using System.Reactive.Disposables;
using Noggog;

namespace SynthEBD;

public class VM : INotifyPropertyChanged, IDisposableDropoff
{
    private readonly CompositeDisposable _compositeDisposable = new();
    public event PropertyChangedEventHandler PropertyChanged;

    // Virtual so subclasses owning unmanaged/GL resources (e.g. VM_CharacterViewer)
    // can release them in addition to tearing down reactive subscriptions.
    public virtual void Dispose() => _compositeDisposable.Dispose();

    /// <summary>Manually raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.
    /// PropertyChanged.Fody only auto-notifies on this object's own auto-property setters, so a computed
    /// or delegating property whose backing data lives on another object (e.g. a row VM's dropdown list
    /// sourced from its parent's collection) has to be signalled by hand when that external source
    /// changes. The non-conventional name keeps Fody from adopting this as its weave-time event invoker.</summary>
    public void ManuallyRaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Add(IDisposable disposable)
    {
        _compositeDisposable.Add(disposable);
    }
}