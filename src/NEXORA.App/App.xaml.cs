using System.Windows;
using NEXORA.App.ViewModels;
using NEXORA.Infrastructure.Settings;

namespace NEXORA.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Корень композиции: здесь собираются зависимости.
        var settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultFilePath());
        var window = new MainWindow { DataContext = new MainViewModel(settingsStore) };
        window.Show();
    }
}
