using System.Text.Json;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Models;

namespace NEXORA.Infrastructure.Storage;

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static void WriteAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ISecretProtector? _protector;

    public JsonSettingsStore(string filePath, ISecretProtector? protector = null) { _path = filePath; _protector = protector; }

    public static string DefaultFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXORA", "settings.json");

    public LauncherSettings Load()
    {
        var s = JsonFile.Read<LauncherSettings>(_path) ?? new LauncherSettings();
        if (_protector is not null && !string.IsNullOrEmpty(s.CurseForgeApiKeyProtected))
        {
            try { s.CurseForgeApiKey = _protector.Unprotect(s.CurseForgeApiKeyProtected); }
            catch (Exception) { s.CurseForgeApiKey = null; }
        }
        return s.Normalize();
    }

    public void Save(LauncherSettings settings)
    {
        settings.Normalize();
        // Без защитника ключ на диск не пишется вообще.
        settings.CurseForgeApiKeyProtected = settings.CurseForgeApiKey is not null && _protector is not null
            ? _protector.Protect(settings.CurseForgeApiKey) : null;
        JsonFile.WriteAtomic(_path, settings);
    }
}

public sealed class JsonProfileStore : IProfileStore
{
    private readonly string _path;
    public JsonProfileStore(string path) => _path = path;
    public List<GameProfile> Load() => JsonFile.Read<List<GameProfile>>(_path) ?? new();
    public void Save(IEnumerable<GameProfile> profiles) => JsonFile.WriteAtomic(_path, profiles.ToList());
}

public sealed class JsonAccountStore : IAccountStore
{
    private sealed class Stored { public string? Token { get; set; } }
    private readonly string _path;
    private readonly ISecretProtector _protector;
    public JsonAccountStore(string path, ISecretProtector protector) { _path = path; _protector = protector; }

    public StoredAccount? Load()
    {
        var s = JsonFile.Read<Stored>(_path);
        if (string.IsNullOrEmpty(s?.Token)) return null;
        try { return new StoredAccount(_protector.Unprotect(s.Token)); }
        catch (Exception) { return null; }
    }

    public void Save(StoredAccount account) => JsonFile.WriteAtomic(_path, new Stored { Token = _protector.Protect(account.RefreshToken) });
    public void Clear() { try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { } }
}
