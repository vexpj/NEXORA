using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using NEXORA.App.Mvvm;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;

namespace NEXORA.App.ViewModels;

public sealed class InstalledModRow : ObservableObject
{
    private bool _enabled;
    public InstalledModRow(InstalledMod m) { FileName = m.FileName; _enabled = m.Enabled; Title = m.Title ?? m.FileName; Version = m.Version ?? ""; }
    public string FileName { get; }
    public string Title { get; }
    public string Version { get; }
    public bool Enabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) { OnPropertyChanged(nameof(ToggleText)); OnPropertyChanged(nameof(Subtitle)); } } }
    public string ToggleText => Enabled ? "Выключить" : "Включить";
    public string Subtitle => (Enabled ? "включён" : "выключен") + (Version.Length > 0 ? "  ·  " + Version : "") + "  ·  " + FileName;
}

public sealed class ModsViewModel : PageViewModel
{
    private readonly AppServices _s;
    private IModCatalog _catalog;
    private string _query = "", _status = "";
    private bool _busy;

    public ModsViewModel(AppServices s) : base("Моды", "\uE7B8")
    {
        _s = s;
        Catalogs = new IModCatalog[] { s.Modrinth, s.CurseForge };
        _catalog = s.Modrinth;
        s.Profiles.PropertyChanged += OnProfilesChanged;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !_busy && CanUseMods, Fail);
        InstallCommand = new AsyncRelayCommand(p => InstallAsync(p as ModProject), () => !_busy && CanUseMods, Fail);
        ToggleCommand = new RelayCommand(p => Toggle(p as InstalledModRow));
        DeleteCommand = new RelayCommand(p => Delete(p as InstalledModRow));
        OpenPageCommand = new RelayCommand(p => { if (p is ModProject m && m.PageUrl is { } u && u.StartsWith("https://")) Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); });
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Profile is not null);
        RefreshInstalled();
        UpdateHint();
    }

    public IReadOnlyList<IModCatalog> Catalogs { get; }
    public ProfilesModel Profiles => _s.Profiles;
    public ObservableCollection<ModProject> Results { get; } = new();
    public ObservableCollection<InstalledModRow> Installed { get; } = new();

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand ToggleCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenPageCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public GameProfile? Profile => Profiles.Selected;
    public bool CanUseMods => Profile is { Loader: not LoaderType.Vanilla };
    public string Query { get => _query; set => SetProperty(ref _query, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public IModCatalog SelectedCatalog { get => _catalog; set { if (SetProperty(ref _catalog, value)) { Results.Clear(); UpdateHint(); } } }

    private void OnProfilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProfilesModel.Selected)) return;
        Results.Clear();
        OnPropertyChanged(nameof(Profile)); OnPropertyChanged(nameof(CanUseMods));
        RefreshInstalled(); UpdateHint();
    }

    private void UpdateHint()
    {
        if (Profile is null) Status = "Выберите профиль (создайте его на вкладке «Версии»).";
        else if (!CanUseMods) Status = "Для модов нужен профиль с загрузчиком Fabric, Forge или NeoForge.";
        else if (!SelectedCatalog.IsAvailable) Status = SelectedCatalog.UnavailableReason ?? "";
        else Status = $"Профиль: {Profile.Display}. Введите название мода и нажмите «Найти».";
    }

    private async Task SearchAsync()
    {
        if (!SelectedCatalog.IsAvailable) { Status = SelectedCatalog.UnavailableReason ?? ""; return; }
        var p = Profile!;
        _busy = true; Status = "Поиск…";
        try
        {
            var list = await SelectedCatalog.SearchAsync(Query.Trim(), p.MinecraftVersion, p.Loader, 0, CancellationToken.None);
            Results.Clear();
            foreach (var m in list) Results.Add(m);
            Status = list.Count == 0 ? "Ничего не найдено для этой версии и загрузчика." : $"Найдено: {list.Count}";
        }
        finally { _busy = false; }
    }

    private async Task InstallAsync(ModProject? m)
    {
        if (m is null) return;
        var p = Profile!;
        _busy = true; Status = $"Подбор файла для «{m.Title}»…";
        try
        {
            var catalog = Catalogs.First(c => c.Name == m.Source);
            var file = await catalog.GetBestFileAsync(m.Id, p.MinecraftVersion, p.Loader, CancellationToken.None);
            if (file is null) { Status = $"Для Minecraft {p.MinecraftVersion} ({p.Loader}) нет подходящей версии мода."; return; }
            Status = $"Загрузка {file.FileName}…";
            var done = await _s.Mods.InstallAsync(p, catalog, file, CancellationToken.None);
            RefreshInstalled();
            Status = "Установлено: " + string.Join(", ", done);
        }
        finally { _busy = false; }
    }

    private void RefreshInstalled()
    {
        Installed.Clear();
        if (Profile is null) return;
        foreach (var m in _s.Mods.List(Profile)) Installed.Add(new InstalledModRow(m));
    }

    private void Toggle(InstalledModRow? row)
    {
        if (row is null || Profile is null) return;
        try { _s.Mods.SetEnabled(Profile, row.FileName, !row.Enabled); RefreshInstalled(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Не удалось переключить мод: " + ex.Message; }
    }

    private void Delete(InstalledModRow? row)
    {
        if (row is null || Profile is null) return;
        try { _s.Mods.Delete(Profile, row.FileName); RefreshInstalled(); Status = "Удалено: " + row.FileName; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Не удалось удалить: " + ex.Message; }
    }

    private void OpenFolder()
    {
        var dir = _s.Paths.ModsDir(Profile!.Id);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void Fail(Exception ex) { Status = "Ошибка: " + ErrorText.From(ex); _busy = false; }
}
