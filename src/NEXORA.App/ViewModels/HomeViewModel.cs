namespace NEXORA.App.ViewModels;

public sealed class HomeViewModel : PageViewModel
{
    public HomeViewModel() : base("Главная", "\uE80F") { }

    public string Welcome => "Добро пожаловать в NEXORA";
    public string VersionStatus => "Версия Minecraft не выбрана";
    public string PlayHint => "Запуск станет доступен после этапов 2–7: каталог версий, вход через Microsoft, загрузка и запуск игры.";
}
