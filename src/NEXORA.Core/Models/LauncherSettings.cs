namespace NEXORA.Core.Models;

/// <summary>Настройки лаунчера, которые сохраняются на диск.</summary>
public sealed class LauncherSettings
{
    public const int MinRamMb = 1024;
    public const int MaxRamMb = 32768;
    public const int DefaultRamMb = 4096;

    public int RamMb { get; set; } = DefaultRamMb;
    public string GameDirectory { get; set; } = DefaultGameDirectory();
    public string? JavaPath { get; set; }

    public static string DefaultGameDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".nexora");

    /// <summary>Приводит значения к допустимым (вызывается после загрузки и перед сохранением).</summary>
    public LauncherSettings Normalize()
    {
        RamMb = Math.Clamp(RamMb, MinRamMb, MaxRamMb);
        if (string.IsNullOrWhiteSpace(GameDirectory))
            GameDirectory = DefaultGameDirectory();
        if (string.IsNullOrWhiteSpace(JavaPath))
            JavaPath = null;
        return this;
    }
}
