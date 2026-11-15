using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NEXORA.Core.Appearance;

namespace NEXORA.App.Themes;

/// <summary>Применяет тему: подменяет кисти приложения (все элементы подхватывают их через DynamicResource) и включает/выключает анимации.</summary>
public static class ThemeManager
{
    private static Style? _fade;

    public static void Apply(AppearanceSettings a)
    {
        var app = Application.Current;
        if (app is null) return;
        var p = ThemePresets.Find(a.ThemeName);
        Set(app, "BackgroundBrush", p.Background);
        Set(app, "SurfaceBrush", p.Surface);
        Set(app, "SecondarySurfaceBrush", p.SecondarySurface);
        Set(app, "AccentBrush", HexColor.IsValid(a.AccentHex) ? a.AccentHex! : p.Accent);
        Set(app, "PrimaryTextBrush", p.PrimaryText);
        Set(app, "SecondaryTextBrush", p.SecondaryText);

        // Анимация появления страниц: у каждой страницы свой экземпляр стиля берётся при её открытии.
        _fade ??= app.Resources["FadeInRoot"] as Style;
        app.Resources["FadeInRoot"] = a.AnimationsEnabled && _fade is not null ? _fade : new Style(typeof(Grid));
    }

    private static void Set(Application app, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        app.Resources[key] = brush;
    }
}
