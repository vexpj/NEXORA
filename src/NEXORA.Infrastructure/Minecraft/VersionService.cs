using System.Text.Json.Nodes;
using NEXORA.Core.Minecraft;
using NEXORA.Infrastructure.Net;

namespace NEXORA.Infrastructure.Minecraft;

public sealed class VersionService
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly LauncherPaths _paths;
    private readonly SafeHttp _http;
    private readonly FileDownloader _dl;
    private List<VersionManifestEntry>? _cache;

    public VersionService(LauncherPaths paths, SafeHttp http, FileDownloader dl) { _paths = paths; _http = http; _dl = dl; }

    public async Task<IReadOnlyList<VersionManifestEntry>> GetManifestAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (_cache is not null && !forceRefresh) return _cache;
        var cacheFile = Path.Combine(_paths.Root, "version_manifest_v2.json");
        string json;
        try
        {
            json = await _http.GetStringAsync(ManifestUrl, ct);
            Directory.CreateDirectory(_paths.Root);
            await File.WriteAllTextAsync(cacheFile, json, ct);
        }
        catch (HttpRequestException) when (File.Exists(cacheFile)) { json = await File.ReadAllTextAsync(cacheFile, ct); }

        _cache = ParseManifest(json);
        return _cache;
    }

    public static List<VersionManifestEntry> ParseManifest(string json)
    {
        var list = new List<VersionManifestEntry>();
        foreach (var v in (JsonNode.Parse(json)?["versions"] as JsonArray) ?? new JsonArray())
        {
            if (v is not JsonObject o) continue;
            list.Add(new VersionManifestEntry(
                o["id"]!.GetValue<string>(), o["type"]!.GetValue<string>(), o["url"]!.GetValue<string>(),
                o["sha1"]?.GetValue<string>(), DateTimeOffset.Parse(o["releaseTime"]!.GetValue<string>())));
        }
        return list;
    }

    /// <summary>Читает JSON версии с диска; если его нет — скачивает из официального манифеста Mojang.</summary>
    public async Task<VersionInfo> LoadAsync(string id, CancellationToken ct)
    {
        var path = _paths.VersionJson(id);
        if (!File.Exists(path))
        {
            var entry = (await GetManifestAsync(ct)).FirstOrDefault(e => e.Id == id)
                ?? throw new FileNotFoundException($"Версия «{id}» не найдена ни локально, ни в каталоге Mojang.");
            await _dl.DownloadAsync(entry.Url, path, entry.Sha1 is null ? null : new FileHash("sha1", entry.Sha1), ct);
        }
        return VersionInfoParser.Parse(await File.ReadAllTextAsync(path, ct));
    }

    public IReadOnlyList<string> InstalledIds() =>
        Directory.Exists(_paths.Versions)
            ? Directory.GetDirectories(_paths.Versions).Select(Path.GetFileName).Where(n => n is not null && File.Exists(_paths.VersionJson(n!))).Select(n => n!).ToList()
            : new List<string>();

    public void Delete(string id)
    {
        var dir = _paths.VersionDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
