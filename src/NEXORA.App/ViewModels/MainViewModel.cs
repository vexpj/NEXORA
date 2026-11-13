using NEXORA.App.Mvvm;

namespace NEXORA.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private PageViewModel _selectedPage;

    public MainViewModel(AppServices s)
    {
        Pages = new PageViewModel[]
        {
            new HomeViewModel(s), new VersionsViewModel(s), new AccountViewModel(s),
            new ModsViewModel(s), new NewsViewModel(s), new SettingsViewModel(s),
        };
        _selectedPage = Pages[0];
        // Тихо восстанавливаем вход по сохранённому токену.
        _ = Task.Run(() => s.Account.RestoreAsync(CancellationToken.None));
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }
}
