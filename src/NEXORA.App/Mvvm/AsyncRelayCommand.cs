using System.Windows.Input;

namespace NEXORA.App.Mvvm;

/// <summary>Асинхронная команда: пока она выполняется, повторный запуск запрещён; ошибки уходят в onError, а не роняют программу.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
        : this(_ => execute(), canExecute, onError) { }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    { _execute = execute; _canExecute = canExecute; _onError = onError; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _execute(parameter); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}
