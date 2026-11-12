using NEXORA.App.Mvvm;

namespace NEXORA.App.ViewModels;

/// <summary>Базовый класс страницы бокового меню.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string glyph)
    {
        Title = title;
        Glyph = glyph;
    }

    public string Title { get; }
    /// <summary>Символ из шрифта Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public string Glyph { get; }
}
