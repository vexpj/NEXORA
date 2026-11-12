using NEXORA.App.Mvvm;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Models;

namespace NEXORA.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _store;
    private int _ramMb;
    private string _gameDirectory;
    private string _javaPath;
    private string _status = string.Empty;

    public SettingsViewModel(ISettingsStore store) : base("Настройки", "\uE713")
    {
        _store = store;
        var s = store.Load();
        _ramMb = s.RamMb;
        _gameDirectory = s.GameDirectory;
        _javaPath = s.JavaPath ?? string.Empty;
        SaveCommand = new RelayCommand(Save);
    }

    public int MinRamMb => LauncherSettings.MinRamMb;
    public int MaxRamMb => LauncherSettings.MaxRamMb;

    public int RamMb { get => _ramMb; set { if (SetProperty(ref _ramMb, value)) Status = string.Empty; } }
    public string GameDirectory { get => _gameDirectory; set { if (SetProperty(ref _gameDirectory, value)) Status = string.Empty; } }
    public string JavaPath { get => _javaPath; set { if (SetProperty(ref _javaPath, value)) Status = string.Empty; } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        try
        {
            var settings = new LauncherSettings { RamMb = RamMb, GameDirectory = GameDirectory, JavaPath = JavaPath }.Normalize();
            _store.Save(settings);
            // Показываем пользователю значения, которые реально сохранены.
            RamMb = settings.RamMb;
            GameDirectory = settings.GameDirectory;
            JavaPath = settings.JavaPath ?? string.Empty;
            Status = "Настройки сохранены";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Не удалось сохранить: " + ex.Message;
        }
    }
}
