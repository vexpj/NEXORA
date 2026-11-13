using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NEXORA.App.ViewModels;

namespace NEXORA.App.Views;

public partial class AccountView : UserControl
{
    public AccountView() => InitializeComponent();

    private void UploadSkin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountViewModel vm) return;
        var dialog = new OpenFileDialog { Title = "Выберите PNG со скином", Filter = "Скин (*.png)|*.png" };
        if (dialog.ShowDialog() == true && vm.UploadSkinCommand.CanExecute(dialog.FileName))
            vm.UploadSkinCommand.Execute(dialog.FileName);
    }
}
