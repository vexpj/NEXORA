using NEXORA.Core.Abstractions;
using NEXORA.Core.Auth;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Auth;

/// <summary>Текущая сессия игрока: вход, автоматическое обновление токена, выход, перезагрузка профиля.</summary>
public sealed class AccountService
{
    private readonly MicrosoftAuthService _auth;
    private readonly IAccountStore _store;
    private readonly Func<string?> _clientId;
    private readonly SafeHttp _http;

    public AccountService(MicrosoftAuthService auth, IAccountStore store, SafeHttp http, Func<string?> clientId)
    { _auth = auth; _store = store; _http = http; _clientId = clientId; }

    public MinecraftSession? Current { get; private set; }
    public event Action? Changed;

    private string RequireClientId() => _clientId() ??
        throw new AuthException("Не указан Client ID приложения Microsoft. Откройте «Настройки» и вставьте его (инструкция в docs/AUTH.md).");

    public async Task<MinecraftSession?> RestoreAsync(CancellationToken ct)
    {
        var stored = _store.Load();
        if (stored is null || _clientId() is not { } cid) return null;
        try { return Set(await _auth.RefreshAsync(cid, stored.RefreshToken, ct)); }
        catch (Exception ex) when (ex is AuthException or HttpRequestException) { return null; }
    }

    public async Task<MinecraftSession> LoginAsync(CancellationToken ct) => Set(await _auth.LoginInteractiveAsync(RequireClientId(), null, ct));

    public async Task<MinecraftSession> EnsureValidAsync(CancellationToken ct)
    {
        var s = Current ?? throw new AuthException("Сначала войдите в аккаунт Microsoft.");
        if (!s.IsExpiringSoon) return s;
        return Set(await _auth.RefreshAsync(RequireClientId(), s.RefreshToken, ct));
    }

    public async Task ReloadProfileAsync(CancellationToken ct)
    {
        var s = await EnsureValidAsync(ct);
        s.Profile = await MinecraftProfileApi.GetProfileAsync(_http, s.AccessToken, ct) ?? s.Profile;
        Changed?.Invoke();
    }

    public void Logout() { _store.Clear(); Current = null; Changed?.Invoke(); }

    public void ApplyProfile(PlayerProfile p) { if (Current is not null) { Current.Profile = p; Changed?.Invoke(); } }

    private MinecraftSession Set(MinecraftSession s)
    {
        if (!string.IsNullOrEmpty(s.RefreshToken)) _store.Save(new StoredAccount(s.RefreshToken));
        Current = s;
        Changed?.Invoke();
        return s;
    }
}
