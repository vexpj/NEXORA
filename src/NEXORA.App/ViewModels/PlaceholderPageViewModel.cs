namespace NEXORA.App.ViewModels;

/// <summary>Страница раздела, чей функционал будет реализован на указанном этапе плана.</summary>
public sealed class PlaceholderPageViewModel : PageViewModel
{
    public PlaceholderPageViewModel(string title, string glyph, int stage, string description)
        : base(title, glyph)
    {
        StageText = $"Этап {stage} плана разработки";
        Description = description;
    }

    public string StageText { get; }
    public string Description { get; }
}
