using System.Text.Json.Serialization;

namespace NEXORA.Core.Models;

/// <summary>Настройки лаунчера, которые сохраняются на диск.</summary>
public sealed class LauncherSettings
{
    public const int MinRamMb = 1024;
    public const int MaxRamMb = 32768;
    public const int DefaultRamMb = 4096;

    public int RamMb { get; set; } = DefaultRamMb;
    /// <summary>Корневая папка данных Minecraft (versions, libraries, assets, runtime, instances).</summary>
    public string GameDirectory { get; set; } = DefaultGameDirectory();
    public string? JavaPath { get; set; }
    /// <summary>Application (client) ID приложения Azure. Это публичный идентификатор, не секрет.</summary>
    public string? MicrosoftClientId { get; set; }
    /// <summary>Личный ключ CurseForge API. В файл пишется только в зашифрованном виде.</summary>
    [JsonIgnore] public string? CurseForgeApiKey { get; set; }
    public string? CurseForgeApiKeyProtected { get; set; }

    public static string DefaultGameDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".nexora");

    public LauncherSettings Normalize()
    {
        RamMb = Math.Clamp(RamMb, MinRamMb, MaxRamMb);
        if (string.IsNullOrWhiteSpace(GameDirectory)) GameDirectory = DefaultGameDirectory();
        JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath.Trim();
        MicrosoftClientId = string.IsNullOrWhiteSpace(MicrosoftClientId) ? null : MicrosoftClientId.Trim();
        CurseForgeApiKey = string.IsNullOrWhiteSpace(CurseForgeApiKey) ? null : CurseForgeApiKey.Trim();
        return this;
    }
}
