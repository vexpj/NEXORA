using System.Text.RegularExpressions;

namespace NEXORA.Core.Appearance;

public sealed record ThemePreset(string Name, string Background, string Surface, string SecondarySurface, string Accent, string PrimaryText, string SecondaryText);

public static class ThemePresets
{
    public static readonly IReadOnlyList<ThemePreset> All = new[]
    {
        new ThemePreset("NEXORA Фиолетовый", "#101014", "#19191F", "#24202D", "#A855F7", "#F4F1F8", "#96929F"),
        new ThemePreset("Океан",             "#0B1220", "#111B2E", "#18263F", "#38BDF8", "#EAF2FB", "#8FA3BF"),
        new ThemePreset("Лес",               "#0E1512", "#15201B", "#1D2B24", "#34D399", "#EAF5EF", "#8FA89B"),
        new ThemePreset("Закат",             "#140F0F", "#1F1616", "#2B1C1C", "#FB7185", "#FBEFEF", "#B39A9A"),
        new ThemePreset("AMOLED чёрная",     "#000000", "#0A0A0A", "#161616", "#A855F7", "#FFFFFF", "#8A8A8A"),
        new ThemePreset("Светлая",           "#F4F2F8", "#FFFFFF", "#ECE8F4", "#7C3AED", "#1B1722", "#6B6478"),
    };

    public static ThemePreset Default => All[0];
    public static ThemePreset Find(string? name) => All.FirstOrDefault(p => p.Name == name) ?? Default;
}

public static class HexColor
{
    private static readonly Regex Rx = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    public static bool IsValid(string? s) => s is not null && Rx.IsMatch(s);
}

/// <summary>Персонализация интерфейса: тема, акцент, фон, анимации, расположение меню.</summary>
public sealed class AppearanceSettings
{
    public string ThemeName { get; set; } = ThemePresets.Default.Name;
    /// <summary>Свой цвет акцента (#RRGGBB) поверх выбранной темы.</summary>
    public string? AccentHex { get; set; }
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.25;
    public bool AnimationsEnabled { get; set; } = true;
    public bool SidebarRight { get; set; }
    public bool CompactSidebar { get; set; }

    public AppearanceSettings Normalize()
    {
        ThemeName = ThemePresets.Find(ThemeName).Name;
        AccentHex = HexColor.IsValid(AccentHex) ? AccentHex!.ToUpperInvariant() : null;
        BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePath) ? null : BackgroundImagePath.Trim();
        BackgroundOpacity = double.IsNaN(BackgroundOpacity) ? 0.25 : Math.Clamp(BackgroundOpacity, 0, 1);
        return this;
    }

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();
}
