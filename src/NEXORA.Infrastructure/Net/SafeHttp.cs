using System.Net;

namespace NEXORA.Infrastructure.Net;

public sealed class UntrustedHostException : Exception
{
    public UntrustedHostException(string host) : base($"Адрес «{host}» не входит в список доверенных. Загрузка заблокирована.") { }
}

/// <summary>Разрешены только HTTPS и заранее известные хосты (Mojang, Microsoft, Fabric, Forge, NeoForge, Modrinth, CurseForge).</summary>
public static class HostPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com", "launcher.mojang.com",
        "launchercontent.mojang.com", "libraries.minecraft.net", "resources.download.minecraft.net",
        "textures.minecraft.net", "api.minecraftservices.com",
        "login.microsoftonline.com", "user.auth.xboxlive.com", "xsts.auth.xboxlive.com",
        "meta.fabricmc.net", "maven.fabricmc.net", "maven.minecraftforge.net", "files.minecraftforge.net",
        "maven.neoforged.net", "repo1.maven.org",
        "api.modrinth.com", "cdn.modrinth.com",
        "api.curseforge.com", "edge.forgecdn.net", "mediafilez.forgecdn.net",
    };

    public static Uri Require(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) throw new UntrustedHostException(uri.Scheme + "://" + uri.Host);
        if (!Allowed.Contains(uri.Host)) throw new UntrustedHostException(uri.Host);
        return uri;
    }
    public static Uri Require(string url) => Require(new Uri(url));
}

public sealed class SafeHttp
{
    private readonly HttpClient _http;

    public SafeHttp(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vexpj/NEXORA/0.2.0 (Minecraft launcher)"); // формат, который просит Modrinth
    }

    /// <summary>GET с ручной проверкой каждого перенаправления.</summary>
    public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct, Action<HttpRequestMessage>? configure = null)
    {
        var uri = HostPolicy.Require(url);
        for (var hop = 0; hop < 6; hop++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            if (hop == 0) configure?.Invoke(req);
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect && resp.Headers.Location is { } loc)
            {
                resp.Dispose();
                uri = HostPolicy.Require(loc.IsAbsoluteUri ? loc : new Uri(uri, loc));
                continue;
            }
            return resp;
        }
        throw new HttpRequestException("Слишком много перенаправлений.");
    }

    public async Task<string> GetStringAsync(string url, CancellationToken ct, Action<HttpRequestMessage>? configure = null)
    {
        using var resp = await GetAsync(url, ct, configure).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Сервер вернул {(int)resp.StatusCode} для {new Uri(url).Host}.", null, resp.StatusCode);
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Для POST/PUT/DELETE (без перенаправлений).</summary>
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        HostPolicy.Require(req.RequestUri!);
        return _http.SendAsync(req, ct);
    }
}
