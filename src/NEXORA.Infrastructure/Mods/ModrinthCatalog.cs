using System.Text.Json.Nodes;
using NEXORA.Core.Models;
using NEXORA.Core.Mods;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Mods;

public sealed class ModrinthCatalog : IModCatalog
{
    private const string Api = "https://api.modrinth.com/v2";
    private readonly SafeHttp _http;
    public ModrinthCatalog(SafeHttp http) => _http = http;

    public string Name => "Modrinth";
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public static string LoaderId(LoaderType l) => l switch { LoaderType.Fabric => "fabric", LoaderType.Forge => "forge", LoaderType.NeoForge => "neoforge", _ => "" };

    public async Task<IReadOnlyList<ModProject>> SearchAsync(string query, string mcVersion, LoaderType loader, int offset, CancellationToken ct)
    {
        var facets = $"[[\"project_type:mod\"],[\"versions:{mcVersion}\"],[\"categories:{LoaderId(loader)}\"]]";
        var url = $"{Api}/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=20&offset={offset}" +
                  $"&index={(string.IsNullOrWhiteSpace(query) ? "downloads" : "relevance")}";
        return ParseSearch(await _http.GetStringAsync(url, ct));
    }

    public static List<ModProject> ParseSearch(string json) =>
        ((JsonNode.Parse(json)?["hits"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(h => new ModProject
        {
            Source = "Modrinth", Id = h["project_id"]!.GetValue<string>(), Title = h["title"]?.GetValue<string>() ?? "",
            Summary = h["description"]?.GetValue<string>() ?? "", Author = h["author"]?.GetValue<string>() ?? "",
            Downloads = h["downloads"]?.GetValue<long>() ?? 0, IconUrl = h["icon_url"]?.GetValue<string>(),
            PageUrl = "https://modrinth.com/mod/" + (h["slug"]?.GetValue<string>() ?? h["project_id"]!.GetValue<string>()),
        }).ToList();

    public async Task<ModFile?> GetBestFileAsync(string projectId, string mcVersion, LoaderType loader, CancellationToken ct)
    {
        var url = $"{Api}/project/{Uri.EscapeDataString(projectId)}/version?game_versions={Uri.EscapeDataString($"[\"{mcVersion}\"]")}&loaders={Uri.EscapeDataString($"[\"{LoaderId(loader)}\"]")}";
        return ParseBestFile(await _http.GetStringAsync(url, ct), projectId);
    }

    public static ModFile? ParseBestFile(string json, string projectId)
    {
        var versions = (JsonNode.Parse(json) as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
        var v = versions.FirstOrDefault(x => x["version_type"]?.GetValue<string>() == "release") ?? versions.FirstOrDefault();
        if (v is null) return null;
        var files = (v["files"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
        var f = files.FirstOrDefault(x => x["primary"]?.GetValue<bool>() == true) ?? files.FirstOrDefault();
        if (f is null) return null;
        return new ModFile
        {
            Source = "Modrinth", ProjectId = projectId, FileName = f["filename"]!.GetValue<string>(), DownloadUrl = f["url"]?.GetValue<string>(),
            Sha1 = f["hashes"]?["sha1"]?.GetValue<string>(), Sha512 = f["hashes"]?["sha512"]?.GetValue<string>(),
            Size = f["size"]?.GetValue<long>() ?? 0, Version = v["version_number"]?.GetValue<string>() ?? "",
            RequiredDependencies = ((v["dependencies"] as JsonArray) ?? new()).OfType<JsonObject>()
                .Where(d => d["dependency_type"]?.GetValue<string>() == "required" && d["project_id"] is not null)
                .Select(d => d["project_id"]!.GetValue<string>()).ToList(),
        };
    }
}
