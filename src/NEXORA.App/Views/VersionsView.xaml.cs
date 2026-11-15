using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NEXORA.App.ViewModels;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Packs;

namespace NEXORA.App.Views;

public partial class VersionsView : UserControl
{
    public VersionsView() => InitializeComponent();

    private VersionsViewModel? ViewModel => DataContext as VersionsViewModel;
    private static GameProfile? ProfileOf(object sender) => (sender as FrameworkElement)?.DataContext as GameProfile;

    private void ExportPack_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ProfileOf(sender) is not { } p) return;
        var safe = string.Concat(p.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить сборку", FileName = safe, DefaultExt = PackService.Extension,
            Filter = "Сборка NEXORA (*" + PackService.Extension + ")|*" + PackService.Extension,
        };
        if (dialog.ShowDialog() == true) ViewModel.ExportPack(p, dialog.FileName);
    }

    private void ImportPackFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new OpenFileDialog { Title = "Открыть сборку", Filter = "Сборка NEXORA (*" + PackService.Extension + ")|*" + PackService.Extension };
        if (dialog.ShowDialog() == true) ViewModel.LoadPackFile(dialog.FileName);
    }

    private void SetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ProfileOf(sender) is not { } p) return;
        var dialog = new OpenFileDialog { Title = "Картинка для профиля", Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog() == true) ViewModel.SetProfileIcon(p, dialog.FileName);
    }
}
