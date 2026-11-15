namespace NEXORA.Core.Auth;

/// <summary>
/// Встроенный Client ID для входа через Microsoft. Игроки ничего не вводят: нажимают «Войти» — открывается сайт Microsoft.
/// Впишите сюда Application (client) ID вашего приложения Azure, ОДОБРЕННОГО Mojang (см. docs/AUTH.md).
/// Это публичный идентификатор, а не секрет: так делают все лаунчеры. Секрет приложения (client secret) сюда не нужен и не вставляется никогда.
/// </summary>
public static class AuthDefaults
{
    public const string DefaultClientId = "";

    /// <summary>Client ID в Azure — это GUID вида xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.</summary>
    public static bool IsValidClientId(string? s) => Guid.TryParseExact(s?.Trim(), "D", out _);

    /// <summary>Свой ID из настроек (для разработчиков) важнее встроенного. null — вход не настроен.</summary>
    public static string? Resolve(string? userValue) =>
        IsValidClientId(userValue) ? userValue!.Trim() : IsValidClientId(DefaultClientId) ? DefaultClientId : null;
}
