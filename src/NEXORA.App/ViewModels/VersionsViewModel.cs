using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Loaders;
using NEXORA.Infrastructure.Minecraft;

namespace NEXORA.App.ViewModels;

public sealed class VersionRow : ObservableObject
{
    private bool _installed;
    public VersionRow(VersionManifestEntry e, bool installed) { Entry = e; _installed = installed; }
    public VersionManifestEntry Entry { get; }
    public string Id => Entry.Id;
    public bool IsInstalled { get => _installed; set { if (SetProperty(ref _installed, value)) OnPropertyChanged(nameof(Display)); } }
    public string Display => $"{Entry.Id}   ·   {TypeName(Entry.Type)}   ·   {Entry.ReleaseTime:yyyy-MM-dd}" + (_installed ? "   ✓" : "");
    private static string TypeName(string t) => t switch { "release" => "релиз", "snapshot" => "снапшот", "old_beta" => "бета", "old_alpha" => "альфа", _ => t };
}

public sealed class VersionsViewModel : PageViewModel
{
    private readonly AppServices _s;
    private List<VersionManifestEntry> _all = new();
    private VersionRow? _selected;
    private bool _showAll, _busy;
    private string _status = "";
    private double _progress;
    private LoaderType _loader = LoaderType.Vanilla;
    private string? _loaderVersion;
    private string _newName = "";
    private int _loaderReq;

    public VersionsViewModel(AppServices s) : base("Версии", "\uE8F1")
    {
        _s = s;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !_busy, Fail);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !_busy && SelectedVersion is not null, Fail);
        DeleteVersionCommand = new RelayCommand(DeleteVersion, () => !_busy && SelectedVersion?.IsInstalled == true);
        CreateProfileCommand = new RelayCommand(CreateProfile, () => SelectedVersion is not null);
        DeleteProfileCommand = new RelayCommand(p => { if (p is GameProfile gp) DeleteProfile(gp); });
        _ = LoadAsync();
    }

    public ObservableCollection<VersionRow> Versions { get; } = new();
    public ObservableCollection<string> LoaderVersions { get; } = new();
    public IReadOnlyList<LoaderType> LoaderTypes { get; } = Enum.GetValues<LoaderType>();
    public ProfilesModel Profiles => _s.Profiles;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand DeleteVersionCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }

    public bool ShowAll { get => _showAll; set { if (SetProperty(ref _showAll, value)) ApplyFilter(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string NewProfileName { get => _newName; set => SetProperty(ref _newName, value); }

    public VersionRow? SelectedVersion
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { _ = LoadLoaderVersionsAsync(); OnPropertyChanged(nameof(CanPickLoaderVersion)); } }
    }

    public LoaderType SelectedLoader
    {
        get => _loader;
        set { if (SetProperty(ref _loader, value)) { _ = LoadLoaderVersionsAsync(); OnPropertyChanged(nameof(CanPickLoaderVersion)); } }
    }

    public string? SelectedLoaderVersion { get => _loaderVersion; set => SetProperty(ref _loaderVersion, value); }
    public bool CanPickLoaderVersion => SelectedLoader != LoaderType.Vanilla && SelectedVersion is not null;

    private async Task LoadAsync()
    {
        _busy = true; Status = "Загрузка каталога версий Mojang…";
        try
        {
            _all = (await _s.Versions.GetManifestAsync(CancellationToken.None, forceRefresh: true)).ToList();
            ApplyFilter();
            Status = $"Версий в каталоге: {_all.Count}";
        }
        finally { _busy = false; }
    }

    private void ApplyFilter()
    {
        var installed = _s.Versions.InstalledIds().ToHashSet();
        var keepId = SelectedVersion?.Id;
        Versions.Clear();
        foreach (var e in _all.Where(e => ShowAll || e.Type == "release"))
            Versions.Add(new VersionRow(e, installed.Contains(e.Id)));
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == keepId) ?? Versions.FirstOrDefault();
    }

    private async Task LoadLoaderVersionsAsync()
    {
        var req = ++_loaderReq;
        LoaderVersions.Clear(); SelectedLoaderVersion = null;
        if (SelectedLoader == LoaderType.Vanilla || SelectedVersion is null) return;
        try
        {
            var list = await _s.Loaders.GetVersionsAsync(SelectedLoader, SelectedVersion.Id, CancellationToken.None);
            if (req != _loaderReq) return;
            foreach (var v in list) LoaderVersions.Add(v);
            SelectedLoaderVersion = LoaderService.PickDefault(list);
            if (list.Count == 0) Status = $"Для Minecraft {SelectedVersion.Id} нет версий {SelectedLoader}.";
        }
        catch (Exception ex) when (req == _loaderReq) { Status = "Не удалось получить версии загрузчика: " + ErrorText.From(ex); }
    }

    private async Task InstallAsync()
    {
        var v = SelectedVersion!;
        _busy = true;
        try
        {
            var progress = new Progress<InstallProgress>(p => { Status = p.Stage; Progress = p.Fraction * 100; });
            await Task.Run(() => _s.Installer.InstallAsync(v.Id, null, progress, CancellationToken.None));
            v.IsInstalled = true;
            Status = $"Minecraft {v.Id} установлен.";
        }
        finally { _busy = false; Progress = 0; }
    }

    private void DeleteVersion()
    {
        var v = SelectedVersion!;
        if (Profiles.Items.Any(p => p.LaunchVersionId == v.Id || p.MinecraftVersion == v.Id))
        { Status = "Версия используется профилем. Сначала удалите профиль."; return; }
        _s.Versions.Delete(v.Id);
        v.IsInstalled = false;
        Status = $"Версия {v.Id} удалена (миры и настройки профилей не затронуты).";
    }

    private void CreateProfile()
    {
        var v = SelectedVersion!;
        var name = string.IsNullOrWhiteSpace(NewProfileName)
            ? (SelectedLoader == LoaderType.Vanilla ? v.Id : $"{v.Id} {SelectedLoader}") : NewProfileName.Trim();
        Profiles.Add(new GameProfile
        {
            Name = name, MinecraftVersion = v.Id, Loader = SelectedLoader,
            LoaderVersion = SelectedLoader == LoaderType.Vanilla ? null : SelectedLoaderVersion,
        });
        NewProfileName = "";
        Status = $"Профиль «{name}» создан. Все файлы скачаются при первом запуске.";
    }

    private void DeleteProfile(GameProfile p)
    {
        Profiles.Remove(p);
        Status = $"Профиль «{p.Name}» удалён. Папка с мирами сохранена: {_s.Paths.InstanceDir(p.Id)}";
    }

    private void Fail(Exception ex) { Status = "Ошибка: " + ErrorText.From(ex); _busy = false; Progress = 0; }
}
