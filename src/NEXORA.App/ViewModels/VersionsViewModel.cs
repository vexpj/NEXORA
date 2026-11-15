using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using NEXORA.Core.Minecraft;
using NEXORA.Core.Models;
using NEXORA.Core.Packs;
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
        LoadPackCodeCommand = new RelayCommand(LoadPackCode);
        ConfirmImportCommand = new AsyncRelayCommand(ImportPackAsync, () => _pendingPack is not null && !_busy, ex => { PackStatus = "Ошибка: " + ErrorText.From(ex); _busy = false; Progress = 0; });
        CancelImportCommand = new RelayCommand(ClearPending);
        CopyShareCodeCommand = new RelayCommand(p => { if (p is GameProfile gp) CopyShareCode(gp); });
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
    public RelayCommand LoadPackCodeCommand { get; }
    public AsyncRelayCommand ConfirmImportCommand { get; }
    public RelayCommand CancelImportCommand { get; }
    public RelayCommand CopyShareCodeCommand { get; }

    // ---------- обмен сборками ----------
    private PackManifest? _pendingPack;
    private string? _pendingZip;
    private string _packCode = "", _packStatus = "";
    private bool _includeConfigs;
    private PackPreview? _pending;

    public PackPreview? Pending { get => _pending; private set { if (SetProperty(ref _pending, value)) OnPropertyChanged(nameof(HasPending)); } }
    public bool HasPending => Pending is not null;
    public string PackCodeInput { get => _packCode; set => SetProperty(ref _packCode, value); }
    public string PackStatus { get => _packStatus; private set => SetProperty(ref _packStatus, value); }
    public bool IncludeConfigs { get => _includeConfigs; set => SetProperty(ref _includeConfigs, value); }

    /// <summary>Открывает файл сборки (двойной щелчок по .nexora-pack или кнопка «Импорт»). Установка начнётся только после подтверждения.</summary>
    public void LoadPackFile(string path)
    {
        try { SetPending(_s.Packs.ReadFile(path), path); }
        catch (InvalidDataException ex) { PackStatus = ex.Message; ClearPending(); }
    }

    private void LoadPackCode()
    {
        try { SetPending(_s.Packs.ReadCode(PackCodeInput), null); }
        catch (InvalidDataException ex) { PackStatus = ex.Message; ClearPending(); }
    }

    private void SetPending(PackManifest m, string? zip)
    {
        _pendingPack = m; _pendingZip = zip;
        var ver = string.IsNullOrEmpty(m.LoaderVersion) ? "" : " " + m.LoaderVersion;
        var mr = m.Mods.Count(x => x.Source == "Modrinth"); var cf = m.Mods.Count(x => x.Source == "CurseForge");
        Pending = new PackPreview(
            $"Сборка «{m.Name}»",
            $"Minecraft {m.MinecraftVersion}  ·  {(m.Loader == LoaderType.Vanilla ? "без модов" : m.Loader + ver)}",
            $"Модов: {m.Mods.Count} (Modrinth: {mr}, CurseForge: {cf}). Каждый файл будет скачан с официального сайта и проверен по контрольной сумме." +
            (m.HasConfigs ? " В сборку включены настройки модов." : ""),
            string.Join(", ", m.Mods.Take(15).Select(x => x.FileName)) + (m.Mods.Count > 15 ? $" и ещё {m.Mods.Count - 15}…" : ""));
        PackStatus = "Проверьте сборку и подтвердите установку.";
    }

    private void ClearPending() { _pendingPack = null; _pendingZip = null; Pending = null; }

    private async Task ImportPackAsync()
    {
        var m = _pendingPack!; var zip = _pendingZip;
        _busy = true;
        try
        {
            var progress = new Progress<InstallProgress>(p => { PackStatus = p.Stage; Progress = p.Fraction * 100; });
            var profile = await Task.Run(() => _s.Packs.ImportAsync(m, zip, progress, CancellationToken.None));
            profile.Name = UniqueName(profile.Name);
            Profiles.Add(profile);
            ClearPending();
            PackStatus = $"Сборка «{profile.Name}» установлена. Minecraft и загрузчик скачаются при первом запуске.";
        }
        finally { _busy = false; Progress = 0; }
    }

    private string UniqueName(string name)
    {
        var n = name; var i = 2;
        while (Profiles.Items.Any(p => p.Name == n)) n = $"{name} ({i++})";
        return n;
    }

    /// <summary>Сохраняет сборку в файл. Вызывается из кода окна после выбора места сохранения.</summary>
    public void ExportPack(GameProfile p, string path)
    {
        try
        {
            var r = _s.Packs.ExportFile(p, path, IncludeConfigs);
            PackStatus = $"Сборка сохранена: модов {r.Exported}" + (r.ConfigFiles > 0 ? $", файлов настроек {r.ConfigFiles}" : "") +
                         (r.SkippedManual.Count > 0 ? $". Не включены моды, добавленные вручную (их нельзя скачать по ссылке): {string.Join(", ", r.SkippedManual.Take(5))}" : "") + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { PackStatus = "Не удалось сохранить: " + ex.Message; }
    }

    private void CopyShareCode(GameProfile p)
    {
        var code = _s.Packs.ExportCode(p, out var skipped);
        PackStatus = Ui.CopyText(code)
            ? $"Код сборки скопирован ({code.Length} символов)." + (code.Length > 1900 ? " Он длинный для чата — отправьте лучше файл." : "") +
              (skipped.Count > 0 ? $" Моды, добавленные вручную, не включены: {skipped.Count}." : "")
            : "Не удалось скопировать: буфер обмена занят другой программой.";
    }

    /// <summary>Копирует выбранную картинку в папку данных лаунчера и назначает её профилю.</summary>
    public void SetProfileIcon(GameProfile p, string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) { Status = "Подходят изображения PNG, JPG, BMP, GIF."; return; }
            if (new FileInfo(sourcePath).Length > 5 * 1024 * 1024) { Status = "Картинка слишком большая (максимум 5 МБ)."; return; }
            var dir = Path.Combine(_s.Paths.DataDir, "icons");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, p.Id + ext);
            File.Copy(sourcePath, dest, overwrite: true);
            p.IconPath = dest;
            Profiles.Touch(p);
            Status = "Картинка профиля обновлена.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "Не удалось сохранить картинку: " + ex.Message; }
    }

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

public sealed record PackPreview(string Title, string Details, string Summary, string ModList);
