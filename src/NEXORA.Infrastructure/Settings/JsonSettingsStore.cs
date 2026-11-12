using System.Text.Json;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Models;

namespace NEXORA.Infrastructure.Settings;

/// <summary>Хранит настройки в JSON-файле. Запись атомарная (временный файл + замена).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _filePath;

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public static string DefaultFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXORA", "settings.json");

    public LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new LauncherSettings().Normalize();

            var json = File.ReadAllText(_filePath);
            return (JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Повреждённый или недоступный файл не должен ронять лаунчер.
            return new LauncherSettings().Normalize();
        }
    }

    public void Save(LauncherSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, _filePath, overwrite: true);
    }
}
