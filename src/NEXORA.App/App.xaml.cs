using System.Windows;
using NEXORA.App.Themes;
using NEXORA.App.ViewModels;
using NEXORA.Infrastructure.Packs;

namespace NEXORA.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "NEXORA — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Корень композиции: здесь собираются зависимости.
        var services = AppServices.Create();
        services.ThemeApplier = ThemeManager.Apply;
        ThemeManager.Apply(services.Settings.Appearance);

        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm };
        window.Show();

        // Двойной щелчок по файлу сборки открывает его в лаунчере (установка — только после подтверждения).
        var pack = e.Args.FirstOrDefault(a => a.EndsWith(PackService.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (pack is not null) vm.OpenPack(pack);
    }
}
