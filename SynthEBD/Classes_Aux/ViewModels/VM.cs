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

    public void Add(IDisposable disposable)
    {
        _compositeDisposable.Add(disposable);
    }
}