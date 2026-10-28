using System;
using System.Windows.Input;

namespace SynthEBD;

/// <summary>Predicate + action <see cref="ICommand"/> used across SynthEBD's view models; requery via
/// <see cref="CommandManager"/>. (Formerly borrowed from the renderer library.)</summary>
public class RelayCommand : ICommand
{
    private readonly Predicate<object> _canExecute;
    private readonly Action<object> _execute;

    public RelayCommand(Predicate<object> canExecute, Action<object> execute)
    {
        _canExecute = canExecute;
        _execute = execute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter)
    {
        return _canExecute(parameter);
    }

    public void Execute(object parameter)
    {
        _execute(parameter);
    }
}