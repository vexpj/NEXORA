using System.Collections.ObjectModel;
using System.Diagnostics;
using NEXORA.App.Mvvm;
using NEXORA.Core.Appearance;
using NEXORA.Core.Auth;
using NEXORA.Core.Models;
using NEXORA.Infrastructure.Minecraft;

namespace NEXORA.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppServices _s;
    private readonly bool _loading;
    private int _ramMb;
    private string _gameDirectory, _javaPath, _clientId, _cfKey, _status = "", _logPreview = "";
    private JavaInstall? _selectedJava;
    // оформление
    private string _theme, _accent, _bg, _appearanceNote = "";
    private double _bgOpacity;
    private bool _anim, _sidebarRight, _compact;

    public SettingsViewModel(AppServices s) : base("Настройки", "\uE713")
    {
        _s = s;
        var c = s.Settings;
        _ramMb = c.RamMb; _gameDirectory = c.GameDirectory; _javaPath = c.JavaPath ?? "";
        _clientId = c.MicrosoftClientId ?? ""; _cfKey = c.CurseForgeApiKey ?? "";
        var a = c.Appearance;
        _theme = a.ThemeName; _accent = a.AccentHex ?? ""; _bg = a.BackgroundImagePath ?? "";
        _bgOpacity = a.BackgroundOpacity; _anim = a.AnimationsEnabled; _sidebarRight = a.SidebarRight; _compact = a.CompactSidebar;
        _loading = false;

        SaveCommand = new RelayCommand(Save);
        DetectJavaCommand = new AsyncRelayCommand(DetectJavaAsync, null, ex => Status = "Ошибка поиска Java: " + ex.Message);
        RefreshLogCommand = new RelayCommand(RefreshLog);
        OpenLogsCommand = new RelayCommand(() => { Directory.CreateDirectory(s.Paths.LogsDir); Process.Start(new ProcessStartInfo(s.Paths.LogsDir) { UseShellExecute = true }); });
        OpenGameDirCommand = new RelayCommand(() => { Directory.CreateDirectory(s.Paths.Root); Process.Start(new ProcessStartInfo(s.Paths.Root) { UseShellExecute = true }); });
        ClearBackgroundCommand = new RelayCommand(() => BackgroundImagePath = "");
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);
        RefreshLog();
    }

    public int MinRamMb => LauncherSettings.MinRamMb;
    public int MaxRamMb => LauncherSettings.MaxRamMb;
    public ObservableCollection<JavaInstall> FoundJava { get; } = new();
    public IReadOnlyList<string> ThemeNames { get; } = ThemePresets.All.Select(p => p.Name).ToList();

    public int RamMb { get => _ramMb; set { if (SetProperty(ref _ramMb, value)) Status = ""; } }
    public string GameDirectory { get => _gameDirectory; set { if (SetProperty(ref _gameDirectory, value)) Status = ""; } }
    public string JavaPath { get => _javaPath; set { if (SetProperty(ref _javaPath, value)) Status = ""; } }
    public string MicrosoftClientId { get => _clientId; set { if (SetProperty(ref _clientId, value)) Status = ""; } }
    public string CurseForgeApiKey { get => _cfKey; set { if (SetProperty(ref _cfKey, value)) Status = ""; } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string LogPreview { get => _logPreview; private set => SetProperty(ref _logPreview, value); }
    public string Version => "NEXORA Launcher " + AppServices.LauncherVersion;

    public JavaInstall? SelectedJava
    {
        get => _selectedJava;
        set { if (SetProperty(ref _selectedJava, value) && value is not null) JavaPath = value.Path; }
    }

    // ---------- оформление (применяется сразу, сохраняется кнопкой «Сохранить») ----------
    public string SelectedTheme { get => _theme; set { if (SetProperty(ref _theme, value)) PreviewAppearance(); } }
    public string AccentHex { get => _accent; set { if (SetProperty(ref _accent, value)) PreviewAppearance(); } }
    public string BackgroundImagePath { get => _bg; set { if (SetProperty(ref _bg, value)) PreviewAppearance(); } }
    public double BackgroundOpacity { get => _bgOpacity; set { if (SetProperty(ref _bgOpacity, value)) PreviewAppearance(); } }
    public bool AnimationsEnabled { get => _anim; set { if (SetProperty(ref _anim, value)) PreviewAppearance(); } }
    public bool SidebarRight { get => _sidebarRight; set { if (SetProperty(ref _sidebarRight, value)) PreviewAppearance(); } }
    public bool CompactSidebar { get => _compact; set { if (SetProperty(ref _compact, value)) PreviewAppearance(); } }
    public string AppearanceNote { get => _appearanceNote; private set => SetProperty(ref _appearanceNote, value); }

    public RelayCommand SaveCommand { get; }
    public AsyncRelayCommand DetectJavaCommand { get; }
    public RelayCommand RefreshLogCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenGameDirCommand { get; }
    public RelayCommand ClearBackgroundCommand { get; }
    public RelayCommand ResetAppearanceCommand { get; }

    private AppearanceSettings BuildAppearance() => new()
    {
        ThemeName = SelectedTheme, AccentHex = AccentHex, BackgroundImagePath = BackgroundImagePath,
        BackgroundOpacity = BackgroundOpacity, AnimationsEnabled = AnimationsEnabled, SidebarRight = SidebarRight, CompactSidebar = CompactSidebar,
    };

    private void PreviewAppearance()
    {
        if (_loading) return;
        AppearanceNote = string.IsNullOrWhiteSpace(AccentHex) || HexColor.IsValid(AccentHex)
            ? "Оформление применено. Нажмите «Сохранить», чтобы оно осталось после перезапуска."
            : "Цвет акцента должен быть в формате #RRGGBB, например #A855F7.";
        _s.ApplyAppearance(BuildAppearance());
    }

    private void ResetAppearance()
    {
        var d = new AppearanceSettings();
        SelectedTheme = d.ThemeName; AccentHex = ""; BackgroundImagePath = ""; BackgroundOpacity = d.BackgroundOpacity;
        AnimationsEnabled = d.AnimationsEnabled; SidebarRight = d.SidebarRight; CompactSidebar = d.CompactSidebar;
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(MicrosoftClientId) && !AuthDefaults.IsValidClientId(MicrosoftClientId))
        { Status = "Client ID выглядит неверно: он должен быть вида xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx."; return; }
        try
        {
            var n = new LauncherSettings
            {
                RamMb = RamMb, GameDirectory = GameDirectory, JavaPath = JavaPath,
                MicrosoftClientId = MicrosoftClientId, CurseForgeApiKey = CurseForgeApiKey,
                Appearance = BuildAppearance(),
            }.Normalize();
            _s.SettingsStore.Save(n);

            var folderChanged = !string.Equals(n.GameDirectory, _s.Settings.GameDirectory, StringComparison.OrdinalIgnoreCase);
            _s.Settings.RamMb = n.RamMb; _s.Settings.JavaPath = n.JavaPath;
            _s.Settings.MicrosoftClientId = n.MicrosoftClientId; _s.Settings.CurseForgeApiKey = n.CurseForgeApiKey;
            _s.Settings.Appearance = n.Appearance;

            RamMb = n.RamMb; JavaPath = n.JavaPath ?? ""; GameDirectory = n.GameDirectory;
            Status = folderChanged ? "Сохранено. Новая папка Minecraft будет использоваться после перезапуска лаунчера." : "Настройки сохранены";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Не удалось сохранить: " + ex.Message;
        }
    }

    private async Task DetectJavaAsync()
    {
        Status = "Поиск Java…";
        var list = await _s.Java.DetectAsync(CancellationToken.None);
        FoundJava.Clear();
        foreach (var j in list) FoundJava.Add(j);
        Status = list.Count == 0 ? "Java не найдена. Оставьте поле пустым — NEXORA загрузит подходящую сама." : $"Найдено установок Java: {list.Count}";
    }

    private void RefreshLog()
    {
        var path = Path.Combine(_s.Paths.LogsDir, "latest.log");
        try
        {
            LogPreview = File.Exists(path)
                ? string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(200))
                : "Журнал пуст: игра ещё не запускалась.";
        }
        catch (IOException) { LogPreview = "Журнал сейчас занят игрой. Обновите позже."; }
    }
}
