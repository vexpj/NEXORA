using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NEXORA.Core.Auth;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Auth;

/// <summary>Microsoft OAuth 2.0 (код + PKCE, вход в браузере) → Xbox Live → XSTS → Minecraft Services. Пароль в лаунчере не вводится.</summary>
public sealed class MicrosoftAuthService
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
    private const string Scope = "XboxLive.signin offline_access";
    private readonly SafeHttp _http;

    public MicrosoftAuthService(SafeHttp http) => _http = http;

    // ---------- PKCE ----------
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));
    public static string CreateChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---------- вход ----------
    public async Task<MinecraftSession> LoginInteractiveAsync(string clientId, Action<string>? openBrowser, CancellationToken ct)
    {
        var verifier = CreateVerifier();
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var port = FreePort();
        var redirect = $"http://localhost:{port}";

        var url = $"{Authority}/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code" +
                  $"&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(Scope)}" +
                  $"&code_challenge={CreateChallenge(verifier)}&code_challenge_method=S256&state={state}&prompt=select_account";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        (openBrowser ?? DefaultOpen)(url);

        var code = await WaitForCodeAsync(listener, state, ct);
        var tok = await TokenAsync(new()
        {
            ["client_id"] = clientId, ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = redirect, ["code_verifier"] = verifier, ["scope"] = Scope,
        }, ct);
        return await CompleteAsync(tok.access, tok.refresh, ct);
    }

    public async Task<MinecraftSession> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        var tok = await TokenAsync(new()
        {
            ["client_id"] = clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["scope"] = Scope,
        }, ct);
        return await CompleteAsync(tok.access, tok.refresh, ct);
    }

    private static void DefaultOpen(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<string> WaitForCodeAsync(HttpListener listener, string state, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var reg = timeout.Token.Register(() => { try { listener.Stop(); } catch (ObjectDisposedException) { } });
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw new AuthException("Время ожидания входа истекло. Попробуйте ещё раз.");
        }

        var q = ctx.Request.QueryString;
        var ok = q["code"] is not null && q["state"] == state;
        var html = ok
            ? "<html><meta charset='utf-8'><body style='background:#101014;color:#F4F1F8;font-family:Segoe UI;text-align:center;padding-top:20vh'><h2>NEXORA</h2><p>Вход выполнен. Эту вкладку можно закрыть.</p></body></html>"
            : "<html><meta charset='utf-8'><body style='background:#101014;color:#F4F1F8;font-family:Segoe UI;text-align:center;padding-top:20vh'><h2>NEXORA</h2><p>Вход не выполнен. Вернитесь в лаунчер.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, ct);
        ctx.Response.Close();

        if (q["error"] is { } err) throw new AuthException("Microsoft отклонил вход: " + (q["error_description"] ?? err));
        if (q["state"] != state) throw new AuthException("Ответ входа не прошёл проверку (state).");
        return q["code"] ?? throw new AuthException("Microsoft не вернул код авторизации.");
    }

    private async Task<(string access, string refresh)> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Post, Authority + "/token") { Content = new FormUrlEncodedContent(form) }, ct);
        var body = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!resp.IsSuccessStatusCode)
            throw new AuthException("Не удалось получить токен Microsoft: " + (body?["error_description"]?.GetValue<string>() ?? resp.StatusCode.ToString()));
        return (body!["access_token"]!.GetValue<string>(), body["refresh_token"]?.GetValue<string>() ?? "");
    }

    // ---------- Xbox → XSTS → Minecraft ----------
    private async Task<MinecraftSession> CompleteAsync(string msAccess, string msRefresh, CancellationToken ct)
    {
        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + msAccess },
            ["RelyingParty"] = "http://auth.xboxlive.com", ["TokenType"] = "JWT",
        }, ct);
        var xblToken = xbl["Token"]!.GetValue<string>();
        var uhs = xbl["DisplayClaims"]!["xui"]![0]!["uhs"]!.GetValue<string>();

        var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject
        {
            ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(xblToken) },
            ["RelyingParty"] = "rp://api.minecraftservices.com/", ["TokenType"] = "JWT",
        }, ct, xstsErrors: true);
        var xstsToken = xsts["Token"]!.GetValue<string>();

        var mc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new JsonObject { ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}" }, ct, mcErrors: true);
        var access = mc["access_token"]!.GetValue<string>();
        var expires = mc["expires_in"]?.GetValue<int>() ?? 86400;

        var profile = await MinecraftProfileApi.GetProfileAsync(_http, access, ct)
            ?? throw new AuthException("На этом аккаунте Microsoft не найден Minecraft: Java Edition. Купите игру или войдите другим аккаунтом.");
        return new MinecraftSession { AccessToken = access, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(expires), RefreshToken = msRefresh, Profile = profile };
    }

    private async Task<JsonNode> PostJsonAsync(string url, JsonObject payload, CancellationToken ct, bool xstsErrors = false, bool mcErrors = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode) return JsonNode.Parse(text)!;

        if (xstsErrors && JsonNode.Parse(text)?["XErr"]?.GetValue<long>() is { } x)
            throw new AuthException(x switch
            {
                2148916233 => "У этой учётной записи Microsoft нет профиля Xbox. Создайте его на xbox.com и повторите вход.",
                2148916235 => "Xbox Live недоступен в вашей стране или регионе.",
                2148916236 or 2148916237 => "Для аккаунта нужно подтвердить возраст на странице Xbox.",
                2148916238 => "Это детская учётная запись: добавьте её в семейную группу Microsoft взрослым.",
                _ => $"Ошибка Xbox Live (XErr {x}).",
            });
        if (mcErrors && (int)resp.StatusCode == 403)
            throw new AuthException("Minecraft отклонил приложение (403). Ваш Azure-клиент должен быть одобрен Mojang: https://aka.ms/mce-reviewappid");
        throw new AuthException($"Ошибка входа ({(int)resp.StatusCode}) на {new Uri(url).Host}.");
    }
}
