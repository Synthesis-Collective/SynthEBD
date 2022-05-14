using System.ComponentModel;
using System.Reactive.Disposables;
using Noggog;

namespace SynthEBD;

public class VM : INotifyPropertyChanged, IDisposableDropoff
{
    private readonly CompositeDisposable _compositeDisposable = new();
    public event PropertyChangedEventHandler PropertyChanged;

    public void Dispose() => _compositeDisposable.Dispose();

    public void Add(IDisposable disposable)
    {
        _compositeDisposable.Add(disposable);
    }
}