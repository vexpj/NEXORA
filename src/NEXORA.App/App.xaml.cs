using System.Windows;
using NEXORA.App.ViewModels;

namespace NEXORA.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Корень композиции: здесь собираются зависимости.
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "NEXORA — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        var window = new MainWindow { DataContext = new MainViewModel(AppServices.Create()) };
        window.Show();
    }
}
