using System;
using System.Windows.Input;

namespace BatchConfigUpdater;

/// <summary>
/// Minimal ICommand used by <see cref="VM_BatchConfigUpdater"/>. Previously resolved from the old
/// Noggog.WPF (0.44-era), whose RelayCommand was removed in the 4.x line the net10 migration pulls in.
/// Kept local so this auxiliary tool stays self-contained (it doesn't reference the CharacterViewer
/// renderer, which now owns the only other copy).
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Predicate<object?> _canExecute;
    private readonly Action<object?> _execute;

    public RelayCommand(Predicate<object?> canExecute, Action<object?> execute)
    {
        _canExecute = canExecute;
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);
}
