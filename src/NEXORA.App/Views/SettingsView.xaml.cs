using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NEXORA.App.ViewModels;

namespace NEXORA.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void BrowseGameDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new OpenFolderDialog { Title = "Выберите папку Minecraft", InitialDirectory = ViewModel.GameDirectory };
        if (dialog.ShowDialog() == true)
            ViewModel.GameDirectory = dialog.FolderName;
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new OpenFileDialog { Title = "Выберите javaw.exe", Filter = "Java (javaw.exe)|javaw.exe|Все файлы|*.*" };
        if (dialog.ShowDialog() == true)
            ViewModel.JavaPath = dialog.FileName;
    }
}
