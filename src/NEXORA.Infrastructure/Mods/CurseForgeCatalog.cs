using System.Text.Json.Nodes;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Mods;

/// <summary>CurseForge требует личный API-ключ (бесплатно на console.curseforge.com). Ключ вводит пользователь, в программу он не встроен.</summary>
public sealed class CurseForgeCatalog : IModCatalog
{
    private const string Api = "https://api.curseforge.com/v1";
    private readonly SafeHttp _http;
    private readonly Func<string?> _apiKey;
    public CurseForgeCatalog(SafeHttp http, Func<string?> apiKey) { _http = http; _apiKey = apiKey; }

    public string Name => "CurseForge";
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey());
    public string? UnavailableReason => IsAvailable ? null : "Укажите API-ключ CurseForge в настройках.";

    private static int LoaderCode(LoaderType l) => l switch { LoaderType.Forge => 1, LoaderType.Fabric => 4, LoaderType.NeoForge => 6, _ => 0 };
    private Action<HttpRequestMessage> Key => r => { r.Headers.Add("x-api-key", _apiKey()); r.Headers.Accept.ParseAdd("application/json"); };

    public async Task<IReadOnlyList<ModProject>> SearchAsync(string query, string mcVersion, LoaderType loader, int offset, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        var url = $"{Api}/mods/search?gameId=432&classId=6&searchFilter={Uri.EscapeDataString(query)}&gameVersion={Uri.EscapeDataString(mcVersion)}" +
                  $"&modLoaderType={LoaderCode(loader)}&pageSize=20&index={offset}&sortField=2&sortOrder=desc";
        return ParseSearch(await _http.GetStringAsync(url, ct, Key));
    }

    public static List<ModProject> ParseSearch(string json) =>
        ((JsonNode.Parse(json)?["data"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(m => new ModProject
        {
            Source = "CurseForge", Id = m["id"]!.GetValue<long>().ToString(), Title = m["name"]?.GetValue<string>() ?? "",
            Summary = m["summary"]?.GetValue<string>() ?? "", Author = (m["authors"] as JsonArray)?.FirstOrDefault()?["name"]?.GetValue<string>() ?? "",
            Downloads = (long)(m["downloadCount"]?.GetValue<double>() ?? 0), IconUrl = m["logo"]?["thumbnailUrl"]?.GetValue<string>(),
            PageUrl = m["links"]?["websiteUrl"]?.GetValue<string>(),
        }).ToList();

    public async Task<ModFile?> GetBestFileAsync(string projectId, string mcVersion, LoaderType loader, CancellationToken ct)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        var url = $"{Api}/mods/{Uri.EscapeDataString(projectId)}/files?gameVersion={Uri.EscapeDataString(mcVersion)}&modLoaderType={LoaderCode(loader)}&pageSize=20";
        return ParseBestFile(await _http.GetStringAsync(url, ct, Key), projectId);
    }

    public static ModFile? ParseBestFile(string json, string projectId)
    {
        var files = ((JsonNode.Parse(json)?["data"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>()
            .OrderByDescending(f => f["fileDate"]?.GetValue<string>() ?? "").ToList();
        var f = files.FirstOrDefault(x => x["releaseType"]?.GetValue<int>() == 1) ?? files.FirstOrDefault();
        if (f is null) return null;
        string? sha1 = ((f["hashes"] as JsonArray) ?? new()).OfType<JsonObject>().FirstOrDefault(h => h["algo"]?.GetValue<int>() == 1)?["value"]?.GetValue<string>();
        return new ModFile
        {
            Source = "CurseForge", ProjectId = projectId, FileName = f["fileName"]!.GetValue<string>(),
            DownloadUrl = f["downloadUrl"]?.GetValue<string>(), Sha1 = sha1, Size = f["fileLength"]?.GetValue<long>() ?? 0,
            Version = f["displayName"]?.GetValue<string>() ?? "",
            RequiredDependencies = ((f["dependencies"] as JsonArray) ?? new()).OfType<JsonObject>()
                .Where(d => d["relationType"]?.GetValue<int>() == 3).Select(d => d["modId"]!.GetValue<long>().ToString()).ToList(),
        };
    }
}
