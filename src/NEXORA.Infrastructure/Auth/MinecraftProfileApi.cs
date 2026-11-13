using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEXORA.Core.Auth;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Auth;

/// <summary>Профиль игрока: получение, смена скина, ника, плаща (api.minecraftservices.com).</summary>
public static class MinecraftProfileApi
{
    private const string Base = "https://api.minecraftservices.com/minecraft/profile";
    private static readonly Regex NameRx = new("^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    public static bool IsValidName(string name) => NameRx.IsMatch(name);

    public static async Task<PlayerProfile?> GetProfileAsync(SafeHttp http, string token, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get, Base, token), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(resp, ct);
        return ParseProfile(await resp.Content.ReadAsStringAsync(ct));
    }

    public static PlayerProfile ParseProfile(string json)
    {
        var n = JsonNode.Parse(json)!;
        var p = new PlayerProfile { Id = n["id"]!.GetValue<string>(), Name = n["name"]!.GetValue<string>() };
        foreach (var s in (n["skins"] as JsonArray) ?? new())
            p.Skins.Add(new SkinInfo(Str(s, "id"), Str(s, "state"), Https(Str(s, "url")), Str(s, "variant")));
        foreach (var c in (n["capes"] as JsonArray) ?? new())
            p.Capes.Add(new CapeInfo(Str(c, "id"), Str(c, "state"), Str(c, "alias"), Https(Str(c, "url"))));
        return p;
    }

    /// <summary>Проверка PNG: подпись и размер 64x64 или 64x32.</summary>
    public static void ValidateSkinPng(byte[] data)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (data.Length < 24 || data.Length > 1_000_000 || !data.Take(8).SequenceEqual(sig))
            throw new InvalidDataException("Файл не является PNG-изображением (или слишком большой).");
        int w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        int h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        if (w != 64 || (h != 64 && h != 32))
            throw new InvalidDataException($"Скин должен быть 64×64 или 64×32 пикселя, а в файле {w}×{h}.");
    }

    public static async Task<PlayerProfile> UploadSkinAsync(SafeHttp http, string token, byte[] png, bool slim, CancellationToken ct)
    {
        ValidateSkinPng(png);
        var form = new MultipartFormDataContent
        {
            { new StringContent(slim ? "slim" : "classic"), "variant" },
        };
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new("image/png");
        form.Add(file, "file", "skin.png");
        var req = Req(HttpMethod.Post, Base + "/skins", token);
        req.Content = form;
        using var resp = await http.SendAsync(req, ct);
        await EnsureAsync(resp, ct);
        return ParseProfile(await resp.Content.ReadAsStringAsync(ct));
    }

    public static async Task<bool> IsNameAvailableAsync(SafeHttp http, string token, string name, CancellationToken ct)
    {
        if (!IsValidName(name)) throw new InvalidDataException("Ник: 3–16 символов, только латиница, цифры и «_».");
        using var resp = await http.SendAsync(Req(HttpMethod.Get, $"{Base}/name/{name}/available", token), ct);
        await EnsureAsync(resp, ct);
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["status"]?.GetValue<string>() == "AVAILABLE";
    }

    public static async Task<PlayerProfile> ChangeNameAsync(SafeHttp http, string token, string name, CancellationToken ct)
    {
        if (!IsValidName(name)) throw new InvalidDataException("Ник: 3–16 символов, только латиница, цифры и «_».");
        using var resp = await http.SendAsync(Req(HttpMethod.Put, $"{Base}/name/{name}", token), ct);
        await EnsureAsync(resp, ct);
        return ParseProfile(await resp.Content.ReadAsStringAsync(ct));
    }

    public static async Task SetCapeAsync(SafeHttp http, string token, string? capeId, CancellationToken ct)
    {
        var req = Req(capeId is null ? HttpMethod.Delete : HttpMethod.Put, Base + "/capes/active", token);
        if (capeId is not null)
            req.Content = new StringContent(new JsonObject { ["capeId"] = capeId }.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        await EnsureAsync(resp, ct);
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, string token)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new("Bearer", token);
        return r;
    }

    private static async Task EnsureAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        var msg = (int)resp.StatusCode switch
        {
            401 => "Сессия истекла. Войдите в аккаунт заново.",
            403 => "Действие запрещено (ник занят или сменить его пока нельзя).",
            400 => "Сервер Minecraft отклонил данные: " + Trim(body),
            429 => "Слишком много запросов. Подождите минуту.",
            _ => $"Ошибка Minecraft Services ({(int)resp.StatusCode}). " + Trim(body),
        };
        throw new AuthException(msg);
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
    private static string Str(JsonNode? n, string k) => n?[k]?.GetValue<string>() ?? "";
    private static string Https(string u) => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + u[7..] : u;
}
