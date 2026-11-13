using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.News;

public sealed record NewsItem(string Title, string Type, string Version, string Date, string Text, string? ImageUrl);

/// <summary>Официальные заметки об обновлениях Minecraft Java Edition (launchercontent.mojang.com).</summary>
public sealed class NewsService
{
    private const string Url = "https://launchercontent.mojang.com/v2/javaPatchNotes.json";
    private readonly SafeHttp _http;
    public NewsService(SafeHttp http) => _http = http;

    public async Task<IReadOnlyList<NewsItem>> GetAsync(CancellationToken ct) => Parse(await _http.GetStringAsync(Url, ct));

    /// <summary>Разбор терпимый к отсутствию полей: формат Mojang не гарантирован.</summary>
    public static List<NewsItem> Parse(string json)
    {
        var list = new List<NewsItem>();
        foreach (var e in ((JsonNode.Parse(json)?["entries"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>())
        {
            var text = e["shortText"]?.GetValue<string>() ?? e["body"]?.GetValue<string>() ?? "";
            text = Regex.Replace(Regex.Replace(text, "<[^>]+>", " "), "\\s+", " ").Trim();
            if (text.Length > 280) text = text[..280] + "…";
            var img = e["image"]?["url"]?.GetValue<string>();
            if (img is not null && img.StartsWith('/')) img = "https://launchercontent.mojang.com" + img;
            if (img is not null && !img.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) img = null;
            list.Add(new NewsItem(e["title"]?.GetValue<string>() ?? e["version"]?.GetValue<string>() ?? "Minecraft",
                e["type"]?.GetValue<string>() ?? "", e["version"]?.GetValue<string>() ?? "",
                e["date"]?.GetValue<string>() is { Length: >= 10 } d ? d[..10] : "", text, img));
        }
        return list.Take(30).ToList();
    }
}
