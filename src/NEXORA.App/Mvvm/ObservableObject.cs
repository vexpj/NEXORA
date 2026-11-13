using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace NEXORA.App.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Выполняет действие в потоке интерфейса (события служб могут приходить из других потоков).</summary>
public static class Ui
{
    public static void Run(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action(); else d.BeginInvoke(action);
    }
}

public static class ErrorText
{
    public static string From(Exception ex) => ex switch
    {
        HttpRequestException h => "Нет связи с сервером или он вернул ошибку: " + h.Message,
        TaskCanceledException => "Время ожидания ответа сервера истекло.",
        _ => ex.Message,
    };
}
