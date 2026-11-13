using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using NEXORA.Infrastructure.News;

namespace NEXORA.App.ViewModels;

public sealed class NewsViewModel : PageViewModel
{
    private readonly AppServices _s;
    private string _status = "Загрузка…";

    public NewsViewModel(AppServices s) : base("Новости", "\uE8A5")
    {
        _s = s;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, null, ex => Status = "Не удалось загрузить новости: " + ErrorText.From(ex));
        RefreshCommand.Execute(null);
    }

    public ObservableCollection<NewsItem> Items { get; } = new();
    public AsyncRelayCommand RefreshCommand { get; }
    public string LauncherInfo => "NEXORA Launcher " + AppServices.LauncherVersion;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private async Task LoadAsync()
    {
        Status = "Загрузка…";
        var list = await _s.News.GetAsync(CancellationToken.None);
        Items.Clear();
        foreach (var n in list) Items.Add(n);
        Status = list.Count == 0 ? "Новостей нет." : "Заметки об обновлениях Minecraft: Java Edition (источник — Mojang).";
    }
}
