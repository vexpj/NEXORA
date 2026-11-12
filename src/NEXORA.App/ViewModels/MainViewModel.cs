using NEXORA.App.Mvvm;
using NEXORA.Core.Abstractions;

namespace NEXORA.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private PageViewModel _selectedPage;

    public MainViewModel(ISettingsStore settingsStore)
    {
        Pages = new PageViewModel[]
        {
            new HomeViewModel(),
            new PlaceholderPageViewModel("Версии", "\uE8F1", 2, "Каталог версий Mojang (Release и Snapshot), установка и удаление версий."),
            new PlaceholderPageViewModel("Аккаунт", "\uE77B", 3, "Вход через Microsoft в браузере (OAuth 2.0 + PKCE) и проверка лицензии Minecraft Java Edition."),
            new PlaceholderPageViewModel("Моды", "\uE7B8", 9, "Fabric, Forge и NeoForge: установка загрузчиков, управление модами и сборками."),
            new PlaceholderPageViewModel("Новости", "\uE8A5", 10, "Новости NEXORA и информация об обновлениях лаунчера."),
            new SettingsViewModel(settingsStore),
        };
        _selectedPage = Pages[0];
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set => SetProperty(ref _selectedPage, value);
    }
}
